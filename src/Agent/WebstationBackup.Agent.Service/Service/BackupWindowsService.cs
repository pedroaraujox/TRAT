using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using WebstationBackup.Agent.Service.Aws;
using WebstationBackup.Agent.Service.Backup;
using WebstationBackup.Agent.Service.Core;
using WebstationBackup.Agent.Service.Logging;
using WebstationBackup.Agent.Service.Security;
using WebstationBackup.Agent.Service.Telemetry;

namespace WebstationBackup.Agent.Service.Service;

internal sealed class BackupWindowsService : ServiceBase
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _mainLoop;

    public BackupWindowsService()
    {
        ServiceName = "WebstationBackupAgent";
    }

    protected override void OnStart(string[] args)
    {
        _mainLoop = Task.Run(() => AgentWorker.RunLoopAsync(new AgentWorkerOptions(), _cts.Token));
    }

    protected override void OnStop()
    {
        _cts.Cancel();
        try
        {
            _mainLoop?.Wait(TimeSpan.FromSeconds(15));
        }
        catch
        {
        }
    }
}

internal sealed class AgentWorkerOptions
{
    public string? RulesPath { get; init; }
    public string? SettingsPath { get; init; }
    public AgentSettings? SettingsOverride { get; init; }
    public string? StateDir { get; init; }
    public bool DryRun { get; init; }
}

internal static class AgentWorker
{
    private sealed class ManualRunContext
    {
        public required string RunRequestId { get; init; }
        public required string TriggerType { get; init; }
        public required DateTimeOffset RequestedAtUtc { get; init; }
    }

    private sealed class ExecutionTargetSettings
    {
        public string? AwsRegion { get; init; }
        public string? S3BucketName { get; init; }
        public string? S3KeyPrefix { get; init; }
        public string? AwsCredentialDpapiProtected { get; init; }
        public string? AwsCredentialTargetName { get; init; }
    }

    public static async Task RunLoopAsync(AgentWorkerOptions options, CancellationToken ct)
    {
        var runtime = BootstrapOrThrow(options);

        while (!ct.IsCancellationRequested)
        {
            var nextRunDelay = TimeSpan.FromMinutes(1);
            try
            {
                var effectivePolicy = await ResolveEffectivePolicyAsync(runtime, ct);
                await SendHeartbeatAsync(runtime.Settings, runtime.ControlPlane, ct);
                await ReportConfigurationIfDueAsync(runtime, options.DryRun, effectivePolicy, ct);

                var manualRun = await TryGetManualRunAsync(runtime, ct);
                if (manualRun is not null)
                {
                    await ExecuteOneJobAsync(runtime, effectivePolicy, options.DryRun, ct, manualRun);
                    await Task.Delay(nextRunDelay, ct);
                    continue;
                }

                var eval = runtime.Schedule.Evaluate(
                    runtime.Rules.Defaults.Schedule.Enabled,
                    runtime.Rules.Defaults.Schedule.Timezone,
                    effectivePolicy.ScheduleDaysOfWeek,
                    effectivePolicy.ScheduleStartTimeLocal,
                    effectivePolicy.MaxRuntimeMinutes,
                    DateTimeOffset.UtcNow);
                if (!eval.ShouldRunNow)
                {
                    await Task.Delay(nextRunDelay, ct);
                    continue;
                }

                var state = runtime.StateStore.Load();
                if (string.Equals(state.LastWindowKey, eval.WindowKeyLocal, StringComparison.Ordinal) &&
                    state.LastAttemptAtUtc.HasValue &&
                    DateTimeOffset.UtcNow - state.LastAttemptAtUtc.Value < TimeSpan.FromMinutes(60))
                {
                    await Task.Delay(nextRunDelay, ct);
                    continue;
                }

                state.LastWindowKey = eval.WindowKeyLocal;
                state.LastAttemptAtUtc = DateTimeOffset.UtcNow;
                runtime.StateStore.Save(state);

                await ExecuteOneJobAsync(runtime, effectivePolicy, options.DryRun, ct, manualRun: null);
            }
            catch (Exception ex)
            {
                runtime.Logger.Error("Execução do job falhou.", ex);
            }

            try
            {
                await Task.Delay(nextRunDelay, ct);
            }
            catch
            {
                return;
            }
        }
    }

    public static async Task RunOnceAsync(AgentWorkerOptions options, CancellationToken ct)
    {
        var runtime = BootstrapOrThrow(options);
        var effectivePolicy = await ResolveEffectivePolicyAsync(runtime, ct);
        await SendHeartbeatAsync(runtime.Settings, runtime.ControlPlane, ct);
        await ReportConfigurationAsync(runtime, options.DryRun, effectivePolicy, ct);
        var manualRun = await TryGetManualRunAsync(runtime, ct);
        await ExecuteOneJobAsync(runtime, effectivePolicy, options.DryRun, ct, manualRun);
    }

    private sealed class Runtime
    {
        public required ProjectRules Rules { get; init; }
        public required AgentSettings Settings { get; init; }
        public required JsonFileLogger Logger { get; init; }
        public required ControlPlaneClient ControlPlane { get; init; }
        public required AgentStateStore StateStore { get; init; }
        public required ScheduleEvaluator Schedule { get; init; }
        public required string StateDir { get; init; }
    }

    private static Runtime BootstrapOrThrow(AgentWorkerOptions options)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var stateDir = string.IsNullOrWhiteSpace(options.StateDir)
            ? Path.Combine(programData, "WebstationBackup", "Agent")
            : Path.GetFullPath(options.StateDir!.Trim());
        Directory.CreateDirectory(stateDir);

        var rulesPath = options.RulesPath;
        if (string.IsNullOrWhiteSpace(rulesPath))
        {
            rulesPath = FindFileUpwards(baseDir, "project.rules.json", maxLevels: 8);
            rulesPath ??= Path.Combine(stateDir, "project.rules.json");
        }
        rulesPath = Path.GetFullPath(rulesPath);

        var statePath = Path.Combine(stateDir, "agent.state.json");

        ProjectRules rules;
        AgentSettings settings;

        var bootstrapLogger = new JsonFileLogger(stateDir, "agent.log.jsonl", maxFileSizeMiB: 50, maxFiles: 10);
        try
        {
            rules = ProjectRules.LoadOrThrow(rulesPath);
            if (options.SettingsOverride is not null)
            {
                settings = options.SettingsOverride;
            }
            else
            {
                var settingsPath = string.IsNullOrWhiteSpace(options.SettingsPath)
                    ? Path.Combine(stateDir, "agent.settings.json")
                    : Path.GetFullPath(options.SettingsPath!.Trim());
                settings = AgentSettings.LoadOrThrow(settingsPath);
            }
        }
        catch (Exception ex)
        {
            bootstrapLogger.Error("Falha no bootstrap do agent (rules/settings).", ex);
            throw;
        }

        var logger = new JsonFileLogger(stateDir, "agent.log.jsonl", rules.Defaults.Logging.Local.MaxFileSizeMiB, rules.Defaults.Logging.Local.MaxFiles);
        var controlPlane = new ControlPlaneClient(settings.ControlPlaneBaseUrl, settings.AgentToken, logger);
        var stateStore = new AgentStateStore(statePath);
        var schedule = new ScheduleEvaluator();

        return new Runtime
        {
            Rules = rules,
            Settings = settings,
            Logger = logger,
            ControlPlane = controlPlane,
            StateStore = stateStore,
            Schedule = schedule,
            StateDir = stateDir
        };
    }

    private static string? FindFileUpwards(string startDir, string fileName, int maxLevels)
    {
        DirectoryInfo? dir;
        try
        {
            dir = new DirectoryInfo(startDir);
            if (!dir.Exists)
            {
                dir = dir.Parent;
            }
        }
        catch
        {
            dir = null;
        }

        for (var i = 0; i <= maxLevels && dir is not null; i++)
        {
            try
            {
                var candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static async Task SendHeartbeatAsync(AgentSettings settings, ControlPlaneClient controlPlane, CancellationToken ct)
    {
        await controlPlane.SendHeartbeatAsync(new
        {
            customerId = settings.CustomerId,
            hostId = settings.HostId,
            hostname = Environment.MachineName,
            osVersion = Environment.OSVersion.VersionString,
            timestampUtc = DateTimeOffset.UtcNow
        }, ct);
    }

    private static async Task ReportConfigurationIfDueAsync(Runtime runtime, bool dryRun, EffectiveRuntimePolicy effectivePolicy, CancellationToken ct)
    {
        var state = runtime.StateStore.Load();
        if (state.LastConfigReportAtUtc.HasValue && DateTimeOffset.UtcNow - state.LastConfigReportAtUtc.Value < TimeSpan.FromMinutes(15))
        {
            return;
        }

        await ReportConfigurationAsync(runtime, dryRun, effectivePolicy, ct);
        state.LastConfigReportAtUtc = DateTimeOffset.UtcNow;
        runtime.StateStore.Save(state);
    }

    private static async Task ReportConfigurationAsync(Runtime runtime, bool dryRun, EffectiveRuntimePolicy effectivePolicy, CancellationToken ct)
    {
        var rules = runtime.Rules;
        var settings = runtime.Settings;
        var logger = runtime.Logger;
        var controlPlane = runtime.ControlPlane;

        var agentVersion = GetAgentVersion();
        var serviceStatus = dryRun ? "DryRun" : "Running";
        var tlsMode = "TLS1.2";

        var now = DateTimeOffset.UtcNow;
        var targetSettings = BuildExecutionTargetSettings(settings, effectivePolicy);

        var precheckOk = true;
        var precheckMessages = new List<string>(capacity: 4);

        if (!Uri.TryCreate(settings.ControlPlaneBaseUrl, UriKind.Absolute, out _))
        {
            precheckOk = false;
            precheckMessages.Add("ControlPlaneBaseUrl inválida.");
        }

        var tlsOk = true;
        try
        {
            if (rules.Defaults.Compatibility.Tls.PrecheckMustFailIfNotSupported)
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
        }
        catch (Exception ex)
        {
            tlsOk = false;
            precheckOk = false;
            precheckMessages.Add("Falha ao habilitar TLS 1.2: " + ex.GetType().Name);
        }

        var stagingPath = "N/A";
        var diskOk = true;

        var credOk = true;
        string? awsCredentialSource = null;
        string? awsAccountId = null;
        string? bucketRegion = null;
        if (dryRun && IsPlaceholderAwsConfiguration(targetSettings))
        {
            credOk = false;
            precheckOk = false;
            precheckMessages.Add("AWS precheck nao executado em dry-run sem configuracao real do host.");
        }
        else
        {
            var awsValidator = new AwsReadinessValidator();
            var awsReadiness = await awsValidator.ValidateAsync(BuildAgentSettingsForAwsValidation(settings, targetSettings), logger, ct);
            credOk = awsReadiness.CredentialOk;
            awsCredentialSource = awsReadiness.CredentialSource;
            awsAccountId = awsReadiness.AwsAccountId;
            bucketRegion = awsReadiness.BucketRegion;

            if (!credOk)
            {
                precheckOk = false;
            }

            if (!string.IsNullOrWhiteSpace(awsReadiness.Message))
            {
                precheckMessages.Add(awsReadiness.Message);
            }
        }

        var message = precheckMessages.Count == 0 ? (precheckOk ? "Prechecks OK." : "Prechecks falharam.") : string.Join(" ", precheckMessages);

        await controlPlane.ReportConfigurationAsync(new
        {
            customerId = settings.CustomerId,
            hostId = settings.HostId,
            effectivePolicyId = effectivePolicy.PolicyId,
            effectivePolicyName = effectivePolicy.PolicyName,
            effectivePolicyKind = effectivePolicy.PolicyKind,
            effectivePolicySource = effectivePolicy.Source,
            effectivePolicyLastChangedAtUtc = effectivePolicy.PolicyLastChangedAtUtc,
            agentVersion,
            serviceStatus,
            tlsMode,
            precheckTlsOk = tlsOk,
            precheckDiskOk = diskOk,
            precheckCredentialOk = credOk,
            stagingPath,
            credentialTargetName = ResolveCredentialReference(targetSettings),
            uploadMode = dryRun ? "dry-run" : "direct-s3",
            timestampUtc = now,
            precheckAtUtc = now,
            precheckMessage = message + " PolicySource=" + effectivePolicy.Source + (string.IsNullOrWhiteSpace(effectivePolicy.PolicyId) ? string.Empty : " PolicyId=" + effectivePolicy.PolicyId)
        }, ct);

        logger.Info("Config report enviado", new Dictionary<string, object?>
        {
            ["serviceStatus"] = serviceStatus,
            ["tlsOk"] = tlsOk,
            ["diskOk"] = diskOk,
            ["credOk"] = credOk,
            ["awsCredentialSource"] = awsCredentialSource,
            ["awsAccountId"] = awsAccountId,
            ["bucketRegion"] = bucketRegion,
            ["stagingPath"] = stagingPath,
            ["policySource"] = effectivePolicy.Source,
            ["policyId"] = effectivePolicy.PolicyId
        });
    }

    private static async Task<EffectiveRuntimePolicy> ResolveEffectivePolicyAsync(Runtime runtime, CancellationToken ct)
    {
        var localPolicy = EffectiveRuntimePolicy.FromLocal(runtime.Rules, runtime.Settings);
        var remotePolicy = await runtime.ControlPlane.TryGetEffectivePolicyAsync(runtime.Settings.CustomerId, runtime.Settings.HostId, ct);
        var effectivePolicy = remotePolicy is null || !remotePolicy.Resolved
            ? localPolicy
            : EffectiveRuntimePolicy.FromRemote(runtime.Rules, runtime.Settings, remotePolicy);

        var state = runtime.StateStore.Load();
        var fingerprint = effectivePolicy.BuildFingerprint();
        if (!string.Equals(state.LastEffectivePolicyFingerprint, fingerprint, StringComparison.Ordinal))
        {
            runtime.Logger.Info("Politica efetiva alterada", new Dictionary<string, object?>
            {
                ["source"] = effectivePolicy.Source,
                ["policyId"] = effectivePolicy.PolicyId,
                ["policyName"] = effectivePolicy.PolicyName,
                ["policyKind"] = effectivePolicy.PolicyKind,
                ["policyLastChangedAtUtc"] = effectivePolicy.PolicyLastChangedAtUtc
            });
            state.LastEffectivePolicyFingerprint = fingerprint;
            state.LastEffectivePolicyObservedAtUtc = DateTimeOffset.UtcNow;
            runtime.StateStore.Save(state);
        }

        return effectivePolicy;
    }

    private static async Task<ManualRunContext?> TryGetManualRunAsync(Runtime runtime, CancellationToken ct)
    {
        var response = await runtime.ControlPlane.TryGetPendingRunRequestAsync(runtime.Settings.CustomerId, runtime.Settings.HostId, ct);
        if (response is null || string.IsNullOrWhiteSpace(response.RunRequestId))
        {
            return null;
        }

        runtime.Logger.Info("Execucao manual recebida do ControlPlane", new Dictionary<string, object?>
        {
            ["runRequestId"] = response.RunRequestId,
            ["triggerType"] = response.TriggerType,
            ["requestedAtUtc"] = response.RequestedAtUtc
        });

        return new ManualRunContext
        {
            RunRequestId = response.RunRequestId.Trim(),
            TriggerType = string.IsNullOrWhiteSpace(response.TriggerType) ? "manual_controlplane" : response.TriggerType.Trim(),
            RequestedAtUtc = response.RequestedAtUtc
        };
    }

    private static ExecutionTargetSettings BuildExecutionTargetSettings(AgentSettings settings, EffectiveRuntimePolicy effectivePolicy)
    {
        return new ExecutionTargetSettings
        {
            AwsRegion = NormalizeOptional(effectivePolicy.AwsRegion) ?? NormalizeOptional(settings.AwsRegion),
            S3BucketName = NormalizeOptional(effectivePolicy.S3BucketName) ?? NormalizeOptional(settings.S3BucketName),
            S3KeyPrefix = NormalizeOptional(effectivePolicy.S3KeyPrefix) ?? NormalizeOptional(settings.S3KeyPrefix),
            AwsCredentialDpapiProtected = NormalizeOptional(settings.AwsCredentialDpapiProtected),
            AwsCredentialTargetName = NormalizeOptional(settings.AwsCredentialTargetName)
        };
    }

    private static AgentSettings BuildAgentSettingsForAwsValidation(AgentSettings baseSettings, ExecutionTargetSettings targetSettings)
    {
        return new AgentSettings
        {
            CustomerId = baseSettings.CustomerId,
            HostId = baseSettings.HostId,
            ControlPlaneBaseUrl = baseSettings.ControlPlaneBaseUrl,
            AgentToken = baseSettings.AgentToken,
            AgentTokenDpapiProtected = baseSettings.AgentTokenDpapiProtected,
            AgentTokenCredentialTargetName = baseSettings.AgentTokenCredentialTargetName,
            AwsRegion = targetSettings.AwsRegion,
            S3BucketName = targetSettings.S3BucketName,
            S3KeyPrefix = targetSettings.S3KeyPrefix,
            AwsCredentialDpapiProtected = targetSettings.AwsCredentialDpapiProtected,
            AwsCredentialTargetName = targetSettings.AwsCredentialTargetName,
            IncludePaths = baseSettings.IncludePaths,
            ExcludePaths = baseSettings.ExcludePaths
        };
    }

    private static string ResolveCredentialReference(ExecutionTargetSettings targetSettings)
    {
        if (!string.IsNullOrWhiteSpace(targetSettings.AwsCredentialDpapiProtected))
        {
            return "DPAPI:LocalMachine";
        }

        return string.IsNullOrWhiteSpace(targetSettings.AwsCredentialTargetName)
            ? "N/A"
            : targetSettings.AwsCredentialTargetName!;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    private static string GetAgentVersion()
    {
        try
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "unknown" : v.ToString();
        }
        catch
        {
            return "unknown";
        }
    }

    private static void PrecheckOrThrow(ProjectRules rules, AgentSettings settings)
    {
        if (rules.Defaults.Compatibility.Tls.PrecheckMustFailIfNotSupported)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        if (!Uri.TryCreate(settings.ControlPlaneBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("ControlPlaneBaseUrl inválida.");
        }
    }

    private static async Task ExecuteOneJobAsync(Runtime runtime, EffectiveRuntimePolicy effectivePolicy, bool dryRun, CancellationToken ct, ManualRunContext? manualRun)
    {
        var rules = runtime.Rules;
        var settings = runtime.Settings;
        var logger = runtime.Logger;
        var controlPlane = runtime.ControlPlane;
        var stateStore = runtime.StateStore;
        var targetSettings = BuildExecutionTargetSettings(settings, effectivePolicy);

        if (!rules.SecurityAndSafety.Aws.NeverDeleteFromS3)
        {
            throw new InvalidOperationException("Config inválida: neverDeleteFromS3 precisa ser true.");
        }

        PrecheckOrThrow(rules, settings);

        if (!dryRun && !IsAwsConfigured(targetSettings))
        {
            logger.Warn("Job bloqueado: configuracao AWS incompleta no host.", new Dictionary<string, object?>
            {
                ["awsRegion"] = targetSettings.AwsRegion,
                ["bucket"] = targetSettings.S3BucketName,
                ["prefix"] = targetSettings.S3KeyPrefix,
                ["credentialTargetName"] = targetSettings.AwsCredentialTargetName
            });
            return;
        }

        if (effectivePolicy.IncludePaths.Length == 0)
        {
            logger.Warn("Job bloqueado: nenhuma IncludePaths efetiva foi definida.", new Dictionary<string, object?>
            {
                ["policySource"] = effectivePolicy.Source,
                ["policyId"] = effectivePolicy.PolicyId
            });
            return;
        }

        var jobId = Guid.NewGuid().ToString("N");
        BackupManifest? manifest = null;
        string? manifestPath = null;
        long uploadedBytes = 0;
        long uploadedItems = 0;
        var finalState = "FAILED";
        string? failureCode = null;
        string? failureMessage = null;

        await controlPlane.StartJobAsync(new
        {
            customerId = settings.CustomerId,
            hostId = settings.HostId,
            jobId,
            startedAtUtc = DateTimeOffset.UtcNow,
            runRequestId = manualRun?.RunRequestId
        }, ct);

        try
        {
            var scanner = new FileScanner();
            var files = scanner.ScanFiles(effectivePolicy.IncludePaths, effectivePolicy.ExcludePaths);

            var builder = new ManifestBuilder();
            manifest = builder.Build(jobId, settings, rules, files);

            manifestPath = builder.SaveToFile(manifest, runtime.StateDir);
            logger.Info("Manifest gerado", new Dictionary<string, object?>
            {
                ["jobId"] = jobId,
                ["plannedBytes"] = manifest.PlannedBytes,
                ["plannedItems"] = manifest.PlannedItems,
                ["dryRun"] = dryRun,
                ["policySource"] = effectivePolicy.Source,
                ["policyId"] = effectivePolicy.PolicyId,
                ["cpuLimitPercent"] = effectivePolicy.CpuLimitPercent,
                ["networkLimitMbit"] = effectivePolicy.NetworkLimitMbit,
                ["triggerType"] = manualRun?.TriggerType ?? "scheduled"
            });

            await controlPlane.ReportProgressAsync(new
            {
                customerId = settings.CustomerId,
                hostId = settings.HostId,
                jobId,
                state = "SCANNING",
                plannedBytes = manifest.PlannedBytes,
                plannedItems = manifest.PlannedItems,
                uploadedBytes = 0,
                uploadedItems = 0,
                timestampUtc = DateTimeOffset.UtcNow
            }, ct);

            if (dryRun)
            {
                uploadedBytes = manifest.PlannedBytes;
                uploadedItems = manifest.PlannedItems;
            }
            else
            {
                var resolvedCredentials = AwsCredentialResolver.ResolveOrThrow(
                    targetSettings.AwsCredentialTargetName,
                    targetSettings.AwsCredentialDpapiProtected);
                logger.Info("Credenciais AWS resolvidas para upload", new Dictionary<string, object?>
                {
                    ["credentialSource"] = resolvedCredentials.Source,
                    ["credentialReference"] = resolvedCredentials.Reference
                });
                var uploader = new S3Uploader(targetSettings.AwsRegion!, targetSettings.S3BucketName!, targetSettings.S3KeyPrefix!, resolvedCredentials.Credentials, logger);

                foreach (var item in manifest.Items)
                {
                    ct.ThrowIfCancellationRequested();
                    await uploader.UploadFileAndVerifyAsync(item.AbsolutePath, item.RelativePath, item.Sha256Base64, ct);
                    uploadedBytes += item.SizeBytes;
                    uploadedItems += 1;

                    if (uploadedItems % 100 == 0)
                    {
                        await controlPlane.ReportProgressAsync(new
                        {
                            customerId = settings.CustomerId,
                            hostId = settings.HostId,
                            jobId,
                            state = "UPLOADING",
                            plannedBytes = manifest.PlannedBytes,
                            plannedItems = manifest.PlannedItems,
                            uploadedBytes,
                            uploadedItems,
                            timestampUtc = DateTimeOffset.UtcNow
                        }, ct);
                    }
                }
            }

            finalState = "SUCCEEDED";
            if (rules.JobDefinition.OkCriteria.BytesPlannedMustEqualBytesConfirmedOnS3 && uploadedBytes != manifest.PlannedBytes)
            {
                finalState = "FAILED";
                failureCode = "BYTES_MISMATCH";
                failureMessage = $"Bytes enviados ({uploadedBytes}) diferem do planejado ({manifest.PlannedBytes}).";
            }

            if (rules.JobDefinition.OkCriteria.CountPlannedMustEqualCountConfirmedOnS3 && uploadedItems != manifest.PlannedItems)
            {
                finalState = "FAILED";
                failureCode = "ITEMS_MISMATCH";
                failureMessage = $"Itens enviados ({uploadedItems}) diferem do planejado ({manifest.PlannedItems}).";
            }
        }
        catch (Exception ex)
        {
            failureCode = ex.GetType().Name.ToUpperInvariant();
            failureMessage = ex.Message;
            logger.Error("Execucao do job falhou.", ex);
        }

        await controlPlane.ReportFinalAsync(new
        {
            customerId = settings.CustomerId,
            hostId = settings.HostId,
            jobId,
            finalState,
            plannedBytes = manifest?.PlannedBytes ?? 0,
            plannedItems = manifest?.PlannedItems ?? 0,
            uploadedBytes,
            uploadedItems,
            failureCode,
            failureMessage,
            finishedAtUtc = DateTimeOffset.UtcNow,
            artifacts = dryRun
                ? new[] { new { type = "MANIFEST_LOCAL", location = manifestPath ?? "N/A" }, new { type = "DRY_RUN", location = "dry-run://no-upload" } }
                : manifestPath is null
                    ? Array.Empty<object>()
                    : new[] { new { type = "MANIFEST_LOCAL", location = manifestPath } },
            runRequestId = manualRun?.RunRequestId
        }, ct);

        var state = stateStore.Load();
        state.LastJobId = jobId;
        state.LastFinalState = finalState;
        stateStore.Save(state);
    }

    private static bool IsPlaceholderAwsConfiguration(ExecutionTargetSettings settings)
    {
        return string.Equals(settings.AwsRegion ?? string.Empty, "dry-run", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(settings.S3BucketName ?? string.Empty, "dry-run", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(settings.AwsCredentialTargetName ?? string.Empty, "dry-run", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAwsConfigured(ExecutionTargetSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.AwsRegion) &&
               !string.IsNullOrWhiteSpace(settings.S3BucketName) &&
               !string.IsNullOrWhiteSpace(settings.S3KeyPrefix);
    }
}
