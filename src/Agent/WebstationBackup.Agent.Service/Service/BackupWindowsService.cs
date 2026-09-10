using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
    private static readonly TimeSpan JobTelemetryHeartbeatInterval = TimeSpan.FromMinutes(1);

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

    private sealed class FinalJobReportPayload
    {
        public required string CustomerId { get; init; }
        public required string HostId { get; init; }
        public required string JobId { get; init; }
        public required string FinalState { get; init; }
        public required long PlannedBytes { get; init; }
        public required long PlannedItems { get; init; }
        public required long UploadedBytes { get; init; }
        public required long UploadedItems { get; init; }
        public string? FailureCode { get; init; }
        public string? FailureMessage { get; init; }
        public required DateTimeOffset FinishedAtUtc { get; init; }
        public required FinalJobArtifactPayload[] Artifacts { get; init; }
        public string? RunRequestId { get; init; }
    }

    private sealed class FinalJobArtifactPayload
    {
        public required string Type { get; init; }
        public required string Location { get; init; }
    }

    private sealed class PersistedFinalJobReport
    {
        public required DateTimeOffset SavedAtUtc { get; init; }
        public required FinalJobReportPayload Payload { get; init; }
    }

    private sealed class PendingFinalJobReport
    {
        public required string FilePath { get; init; }
        public required PersistedFinalJobReport Content { get; init; }
    }

    private sealed class JobTelemetrySnapshot
    {
        private readonly object _sync = new();
        private string _state;
        private long _plannedBytes;
        private long _plannedItems;
        private long _uploadedBytes;
        private long _uploadedItems;

        public JobTelemetrySnapshot(string initialState)
        {
            _state = string.IsNullOrWhiteSpace(initialState) ? "STARTED" : initialState.Trim();
        }

        public void Update(string state, long plannedBytes, long plannedItems, long uploadedBytes, long uploadedItems)
        {
            lock (_sync)
            {
                _state = string.IsNullOrWhiteSpace(state) ? _state : state.Trim();
                _plannedBytes = Math.Max(0, plannedBytes);
                _plannedItems = Math.Max(0, plannedItems);
                _uploadedBytes = Math.Max(0, uploadedBytes);
                _uploadedItems = Math.Max(0, uploadedItems);
            }
        }

        public (string State, long PlannedBytes, long PlannedItems, long UploadedBytes, long UploadedItems) Capture()
        {
            lock (_sync)
            {
                return (_state, _plannedBytes, _plannedItems, _uploadedBytes, _uploadedItems);
            }
        }
    }

    public static async Task RunLoopAsync(AgentWorkerOptions options, CancellationToken ct)
    {
        var runtime = BootstrapOrThrow(options);
        var configurationReportedThisRun = false;

        while (!ct.IsCancellationRequested)
        {
            var nextRunDelay = TimeSpan.FromMinutes(1);
            try
            {
                await FlushPendingFinalReportsAsync(runtime, ct);
                var effectivePolicy = await ResolveEffectivePolicyAsync(runtime, ct);
                await SendHeartbeatAsync(runtime.Settings, runtime.ControlPlane, ct);
                if (!configurationReportedThisRun)
                {
                    await ReportConfigurationAsync(runtime, options.DryRun, effectivePolicy, ct);
                    var configReportState = runtime.StateStore.Load();
                    configReportState.LastConfigReportAtUtc = DateTimeOffset.UtcNow;
                    runtime.StateStore.Save(configReportState);
                    configurationReportedThisRun = true;
                }
                else
                {
                    await ReportConfigurationIfDueAsync(runtime, options.DryRun, effectivePolicy, ct);
                }

                var manualRun = await TryGetManualRunAsync(runtime, ct);
                if (manualRun is not null)
                {
                    if (string.Equals(manualRun.TriggerType, "aws_check", StringComparison.OrdinalIgnoreCase))
                    {
                        await ProcessAwsCheckAsync(runtime, options.DryRun, effectivePolicy, manualRun, ct);
                        await Task.Delay(nextRunDelay, ct);
                        continue;
                    }

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
        await FlushPendingFinalReportsAsync(runtime, ct);
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
            ? Path.Combine(programData, "TRAT", "Agent")
            : Path.GetFullPath(options.StateDir!.Trim());
        if (string.IsNullOrWhiteSpace(options.StateDir))
        {
            var legacyStateDir = Path.Combine(programData, "WebstationBackup", "Agent");
            TryMigrateLegacyState(legacyStateDir, stateDir);
        }
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

    private static void TryMigrateLegacyState(string legacyStateDir, string preferredStateDir)
    {
        try
        {
            if (!Directory.Exists(legacyStateDir))
            {
                return;
            }

            Directory.CreateDirectory(preferredStateDir);

            CopyIfMissing(
                Path.Combine(legacyStateDir, "agent.settings.json"),
                Path.Combine(preferredStateDir, "agent.settings.json"));
            CopyIfMissing(
                Path.Combine(legacyStateDir, "project.rules.json"),
                Path.Combine(preferredStateDir, "project.rules.json"));
            CopyIfMissing(
                Path.Combine(legacyStateDir, "agent.state.json"),
                Path.Combine(preferredStateDir, "agent.state.json"));
        }
        catch
        {
        }
    }

    private static void CopyIfMissing(string source, string destination)
    {
        try
        {
            if (!File.Exists(destination) && File.Exists(source))
            {
                File.Copy(source, destination, overwrite: false);
            }
        }
        catch
        {
        }
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
        var fileScanner = new FileScanner();
        var configuredPathValidation = fileScanner.ValidateConfiguredPaths(effectivePolicy.IncludePaths, effectivePolicy.ExcludePaths);
        var blockingPathIssues = configuredPathValidation.Issues.Where(i => i.IsBlocking).ToArray();
        if (blockingPathIssues.Length > 0)
        {
            diskOk = false;
            precheckOk = false;
            precheckMessages.Add(BuildIssueSummary(blockingPathIssues));
            logger.Warn("Precheck de paths encontrou problemas bloqueantes.", new Dictionary<string, object?>
            {
                ["issueCount"] = blockingPathIssues.Length,
                ["issues"] = blockingPathIssues.Select(i => new { i.Code, i.Path, i.Message }).ToArray()
            });
        }
        else if (configuredPathValidation.Issues.Count > 0)
        {
            precheckMessages.Add(BuildIssueSummary(configuredPathValidation.Issues));
            logger.Warn("Precheck de paths encontrou alertas nao bloqueantes.", new Dictionary<string, object?>
            {
                ["issueCount"] = configuredPathValidation.Issues.Count,
                ["issues"] = configuredPathValidation.Issues.Select(i => new { i.Code, i.Path, i.Message }).ToArray()
            });
        }

        var credOk = true;
        string? awsCredentialSource = null;
        string? awsAccountId = null;
        string? bucketRegion = null;
        IReadOnlyList<string> availableBuckets = Array.Empty<string>();
        IReadOnlyDictionary<string, string> availableBucketRegions = new Dictionary<string, string>();
        bool? bucketDiscoveryOk = null;
        string? bucketDiscoveryMessage = null;
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
            availableBuckets = awsReadiness.AvailableBuckets;
            availableBucketRegions = awsReadiness.AvailableBucketRegions;
            bucketDiscoveryOk = awsReadiness.BucketDiscoveryOk;
            bucketDiscoveryMessage = awsReadiness.BucketDiscoveryMessage;

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
            precheckMessage = message + " PolicySource=" + effectivePolicy.Source + (string.IsNullOrWhiteSpace(effectivePolicy.PolicyId) ? string.Empty : " PolicyId=" + effectivePolicy.PolicyId),
            awsAccountId,
            bucketDiscoveryOk,
            bucketDiscoveryMessage,
            availableBuckets,
            availableBucketRegions
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
            ["availableBucketCount"] = availableBuckets.Count,
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
            var assembly = Assembly.GetExecutingAssembly();
            var informational = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), inherit: false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                return informational!;
            }

            var v = assembly.GetName().Version;
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

    private static async Task FlushPendingFinalReportsAsync(Runtime runtime, CancellationToken ct)
    {
        foreach (var pendingReport in LoadPendingFinalReports(runtime))
        {
            try
            {
                await runtime.ControlPlane.ReportFinalAsync(pendingReport.Content.Payload, ct);
                DeletePendingFinalReport(pendingReport.FilePath, runtime.Logger);
                runtime.Logger.Info("Report final pendente reenviado com sucesso.", new Dictionary<string, object?>
                {
                    ["jobId"] = pendingReport.Content.Payload.JobId,
                    ["filePath"] = pendingReport.FilePath,
                    ["savedAtUtc"] = pendingReport.Content.SavedAtUtc
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                runtime.Logger.Warn("Falha ao reenviar report final pendente.", new Dictionary<string, object?>
                {
                    ["jobId"] = pendingReport.Content.Payload.JobId,
                    ["filePath"] = pendingReport.FilePath,
                    ["exceptionType"] = ex.GetType().FullName,
                    ["message"] = ex.Message
                });
                return;
            }
        }
    }

    private static IReadOnlyList<PendingFinalJobReport> LoadPendingFinalReports(Runtime runtime)
    {
        var outboxDir = GetFinalReportOutboxDirectory(runtime.StateDir);
        if (!Directory.Exists(outboxDir))
        {
            return Array.Empty<PendingFinalJobReport>();
        }

        var results = new List<PendingFinalJobReport>();
        foreach (var filePath in Directory.GetFiles(outboxDir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var raw = File.ReadAllText(filePath);
                var content = JsonConvert.DeserializeObject<PersistedFinalJobReport>(raw);
                if (content?.Payload is null)
                {
                    throw new InvalidOperationException("Arquivo de outbox sem payload válido.");
                }

                results.Add(new PendingFinalJobReport
                {
                    FilePath = filePath,
                    Content = content
                });
            }
            catch (Exception ex)
            {
                runtime.Logger.Warn("Arquivo de outbox de report final ignorado por falha de leitura.", new Dictionary<string, object?>
                {
                    ["filePath"] = filePath,
                    ["exceptionType"] = ex.GetType().FullName,
                    ["message"] = ex.Message
                });
                MarkPendingFinalReportAsCorrupted(filePath, runtime.Logger);
            }
        }

        return results;
    }

    private static async Task TrySendOrPersistFinalReportAsync(Runtime runtime, FinalJobReportPayload payload, CancellationToken ct)
    {
        try
        {
            await runtime.ControlPlane.ReportFinalAsync(payload, ct);
        }
        catch (Exception ex)
        {
            var pendingPath = PersistPendingFinalReport(runtime, payload);
            runtime.Logger.Error("Falha ao enviar report final; payload persistido para reenvio.", ex, new Dictionary<string, object?>
            {
                ["jobId"] = payload.JobId,
                ["filePath"] = pendingPath,
                ["finalState"] = payload.FinalState
            });
        }
    }

    private static string PersistPendingFinalReport(Runtime runtime, FinalJobReportPayload payload)
    {
        var outboxDir = GetFinalReportOutboxDirectory(runtime.StateDir);
        Directory.CreateDirectory(outboxDir);

        var fileName = string.Format(
            "job-final-{0:yyyyMMddHHmmssfff}-{1}.json",
            payload.FinishedAtUtc.UtcDateTime,
            payload.JobId);
        var filePath = Path.Combine(outboxDir, fileName);
        var persisted = new PersistedFinalJobReport
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Payload = payload
        };

        WriteJsonAtomically(filePath, persisted);
        return filePath;
    }

    private static string GetFinalReportOutboxDirectory(string stateDir)
    {
        return Path.Combine(stateDir, "outbox", "job-final");
    }

    private static void WriteJsonAtomically(string path, object payload)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonConvert.SerializeObject(payload, Formatting.Indented));
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }

    private static void DeletePendingFinalReport(string filePath, JsonFileLogger logger)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            logger.Warn("Nao foi possivel remover arquivo de outbox de report final ja reenviado.", new Dictionary<string, object?>
            {
                ["filePath"] = filePath,
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message
            });
        }
    }

    private static void MarkPendingFinalReportAsCorrupted(string filePath, JsonFileLogger logger)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var corruptedPath = filePath + ".corrupt";
            if (File.Exists(corruptedPath))
            {
                File.Delete(corruptedPath);
            }

            File.Move(filePath, corruptedPath);
        }
        catch (Exception ex)
        {
            logger.Warn("Nao foi possivel isolar arquivo corrompido do outbox de report final.", new Dictionary<string, object?>
            {
                ["filePath"] = filePath,
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message
            });
        }
    }

    private static Task StartJobTelemetryLoopAsync(Runtime runtime, string jobId, JobTelemetrySnapshot snapshot, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(JobTelemetryHeartbeatInterval, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                var telemetry = snapshot.Capture();
                try
                {
                    await SendHeartbeatAsync(runtime.Settings, runtime.ControlPlane, ct);
                    await runtime.ControlPlane.ReportProgressAsync(new
                    {
                        customerId = runtime.Settings.CustomerId,
                        hostId = runtime.Settings.HostId,
                        jobId,
                        state = telemetry.State,
                        plannedBytes = telemetry.PlannedBytes,
                        plannedItems = telemetry.PlannedItems,
                        uploadedBytes = telemetry.UploadedBytes,
                        uploadedItems = telemetry.UploadedItems,
                        timestampUtc = DateTimeOffset.UtcNow
                    }, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    runtime.Logger.Warn("Falha ao publicar telemetria periodica do job.", new Dictionary<string, object?>
                    {
                        ["jobId"] = jobId,
                        ["state"] = telemetry.State,
                        ["exceptionType"] = ex.GetType().FullName,
                        ["message"] = ex.Message
                    });
                }
            }
        }, CancellationToken.None);
    }

    private static async Task StopJobTelemetryLoopAsync(CancellationTokenSource? cts, Task? task, JsonFileLogger logger, string jobId)
    {
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }
            finally
            {
                cts.Dispose();
            }
        }

        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (Exception ex)
        {
            logger.Warn("Loop de telemetria do job terminou com erro.", new Dictionary<string, object?>
            {
                ["jobId"] = jobId,
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message
            });
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
            const string blockedMessage = "A politica recebida do ControlPlane nao possui regiao, bucket e prefixo S3 completos.";
            logger.Warn("Job bloqueado: configuracao AWS incompleta no host.", new Dictionary<string, object?>
            {
                ["awsRegion"] = targetSettings.AwsRegion,
                ["bucket"] = targetSettings.S3BucketName,
                ["prefix"] = targetSettings.S3KeyPrefix,
                ["credentialTargetName"] = targetSettings.AwsCredentialTargetName
            });
            await ReportBlockedJobAsync(runtime, manualRun, "AWS_POLICY_INCOMPLETE", blockedMessage, ct);
            return;
        }

        if (effectivePolicy.IncludePaths.Length == 0)
        {
            const string blockedMessage = "A politica recebida do ControlPlane nao possui pastas para backup.";
            logger.Warn("Job bloqueado: nenhuma IncludePaths efetiva foi definida.", new Dictionary<string, object?>
            {
                ["policySource"] = effectivePolicy.Source,
                ["policyId"] = effectivePolicy.PolicyId
            });
            await ReportBlockedJobAsync(runtime, manualRun, "BACKUP_PATHS_MISSING", blockedMessage, ct);
            return;
        }

        if (!dryRun)
        {
            var awsValidator = new AwsReadinessValidator();
            var awsReadiness = await awsValidator.ValidateAsync(
                BuildAgentSettingsForAwsValidation(settings, targetSettings),
                logger,
                ct);
            if (!awsReadiness.CredentialOk)
            {
                var blockedMessage = string.IsNullOrWhiteSpace(awsReadiness.Message)
                    ? "O destino AWS configurado na politica nao esta disponivel."
                    : awsReadiness.Message;
                logger.Warn("Job bloqueado: destino AWS indisponivel no precheck.", new Dictionary<string, object?>
                {
                    ["bucket"] = targetSettings.S3BucketName,
                    ["region"] = targetSettings.AwsRegion,
                    ["reason"] = blockedMessage
                });
                await ReportBlockedJobAsync(runtime, manualRun, "AWS_DESTINATION_UNAVAILABLE", blockedMessage, ct);
                return;
            }
        }

        var jobId = Guid.NewGuid().ToString("N");
        BackupManifest? manifest = null;
        string? manifestPath = null;
        string? issueReportPath = null;
        long uploadedBytes = 0;
        long uploadedItems = 0;
        var finalState = "FAILED";
        string? failureCode = null;
        string? failureMessage = null;
        var processingIssues = new List<BackupProcessingIssue>();
        var telemetrySnapshot = new JobTelemetrySnapshot("STARTED");
        CancellationTokenSource? telemetryCts = null;
        Task? telemetryTask = null;

        await controlPlane.StartJobAsync(new
        {
            customerId = settings.CustomerId,
            hostId = settings.HostId,
            jobId,
            startedAtUtc = DateTimeOffset.UtcNow,
            runRequestId = manualRun?.RunRequestId
        }, ct);

        telemetryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        telemetryTask = StartJobTelemetryLoopAsync(runtime, jobId, telemetrySnapshot, telemetryCts.Token);

        try
        {
            var scanner = new FileScanner();
            var scanResult = scanner.ScanFiles(effectivePolicy.IncludePaths, effectivePolicy.ExcludePaths);
            processingIssues.AddRange(scanResult.Issues);

            var builder = new ManifestBuilder();
            var buildResult = builder.Build(jobId, settings, rules, scanResult.Files);
            manifest = buildResult.Manifest;
            processingIssues.AddRange(buildResult.Issues);

            manifestPath = builder.SaveToFile(manifest, runtime.StateDir);
            telemetrySnapshot.Update("SCANNING", manifest.PlannedBytes, manifest.PlannedItems, uploadedBytes, uploadedItems);
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
                ["triggerType"] = manualRun?.TriggerType ?? "scheduled",
                ["issueCount"] = processingIssues.Count
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
                telemetrySnapshot.Update("FINALIZING", manifest.PlannedBytes, manifest.PlannedItems, uploadedBytes, uploadedItems);
            }
            else
            {
                var retrySettings = rules.Defaults.Upload.Retry;
                var resolvedCredentials = AwsCredentialResolver.ResolveOrThrow(
                    targetSettings.AwsCredentialTargetName,
                    targetSettings.AwsCredentialDpapiProtected);
                logger.Info("Credenciais AWS resolvidas para upload", new Dictionary<string, object?>
                {
                    ["credentialSource"] = resolvedCredentials.Source,
                    ["credentialReference"] = resolvedCredentials.Reference
                });
                var uploader = new S3Uploader(
                    targetSettings.AwsRegion!,
                    targetSettings.S3BucketName!,
                    targetSettings.S3KeyPrefix!,
                    resolvedCredentials.Credentials,
                    logger,
                    Math.Max(1, retrySettings.MaxAttempts),
                    TimeSpan.FromSeconds(Math.Max(0, retrySettings.InitialDelaySeconds)),
                    TimeSpan.FromSeconds(Math.Max(retrySettings.InitialDelaySeconds, retrySettings.MaxDelaySeconds)));

                foreach (var item in manifest.Items)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var alreadyExists = await uploader.FileAlreadyExistsWithSameSha256Async(item.RelativePath, item.Sha256Base64, ct);
                        if (alreadyExists)
                        {
                            logger.Info("Arquivo já existe no S3 com mesmo hash; pulando upload.", new Dictionary<string, object?>
                            {
                                ["jobId"] = jobId,
                                ["relativePath"] = item.RelativePath,
                                ["sha256b64"] = item.Sha256Base64
                            });
                            uploadedBytes += item.SizeBytes;
                            uploadedItems += 1;
                            continue;
                        }

                        await uploader.UploadFileAndVerifyAsync(item.AbsolutePath, item.RelativePath, item.Sha256Base64, ct);
                        uploadedBytes += item.SizeBytes;
                        uploadedItems += 1;
                    }
                    catch (Exception ex)
                    {
                        var issue = CreateProcessingIssue("upload", item.AbsolutePath, BuildIssueCode(ex), $"Arquivo ignorado durante upload. {ex.Message}", isBlocking: true);
                        processingIssues.Add(issue);
                        logger.Warn("Arquivo ignorado durante upload.", new Dictionary<string, object?>
                        {
                            ["jobId"] = jobId,
                            ["path"] = item.AbsolutePath,
                            ["relativePath"] = item.RelativePath,
                            ["exceptionType"] = ex.GetType().FullName,
                            ["message"] = ex.Message
                        });
                        continue;
                    }

                    telemetrySnapshot.Update("UPLOADING", manifest.PlannedBytes, manifest.PlannedItems, uploadedBytes, uploadedItems);
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

            if (processingIssues.Any(i => i.IsBlocking))
            {
                finalState = "FAILED";
                failureCode = BuildFailureCode(processingIssues);
                failureMessage = BuildIssueSummary(processingIssues);
            }
            else
            {
                finalState = "SUCCEEDED";
            }

            if (finalState == "SUCCEEDED" && rules.JobDefinition.OkCriteria.BytesPlannedMustEqualBytesConfirmedOnS3 && uploadedBytes != manifest.PlannedBytes)
            {
                finalState = "FAILED";
                failureCode = "BYTES_MISMATCH";
                failureMessage = $"Bytes enviados ({uploadedBytes}) diferem do planejado ({manifest.PlannedBytes}).";
            }

            if (finalState == "SUCCEEDED" && rules.JobDefinition.OkCriteria.CountPlannedMustEqualCountConfirmedOnS3 && uploadedItems != manifest.PlannedItems)
            {
                finalState = "FAILED";
                failureCode = "ITEMS_MISMATCH";
                failureMessage = $"Itens enviados ({uploadedItems}) diferem do planejado ({manifest.PlannedItems}).";
            }

            issueReportPath = SaveIssueReportIfNeeded(jobId, runtime.StateDir, processingIssues);
        }
        catch (Exception ex)
        {
            failureCode = ex.GetType().Name.ToUpperInvariant();
            failureMessage = ex.Message;
            telemetrySnapshot.Update("FAILED", manifest?.PlannedBytes ?? 0, manifest?.PlannedItems ?? 0, uploadedBytes, uploadedItems);
            logger.Error("Execucao do job falhou.", ex);
        }
        telemetrySnapshot.Update(finalState, manifest?.PlannedBytes ?? 0, manifest?.PlannedItems ?? 0, uploadedBytes, uploadedItems);
        await StopJobTelemetryLoopAsync(telemetryCts, telemetryTask, logger, jobId);

        var finalReport = new FinalJobReportPayload
        {
            CustomerId = settings.CustomerId,
            HostId = settings.HostId,
            JobId = jobId,
            FinalState = finalState,
            PlannedBytes = manifest?.PlannedBytes ?? 0,
            PlannedItems = manifest?.PlannedItems ?? 0,
            UploadedBytes = uploadedBytes,
            UploadedItems = uploadedItems,
            FailureCode = failureCode,
            FailureMessage = failureMessage,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            Artifacts = BuildArtifacts(manifestPath, issueReportPath, dryRun),
            RunRequestId = manualRun?.RunRequestId
        };
        await TrySendOrPersistFinalReportAsync(runtime, finalReport, ct);

        var state = stateStore.Load();
        state.LastJobId = jobId;
        state.LastFinalState = finalState;
        stateStore.Save(state);
    }

    private static async Task ProcessAwsCheckAsync(Runtime runtime, bool dryRun, EffectiveRuntimePolicy effectivePolicy, ManualRunContext request, CancellationToken ct)
    {
        try
        {
            await ReportConfigurationAsync(runtime, dryRun, effectivePolicy, ct);
            var state = runtime.StateStore.Load();
            state.LastConfigReportAtUtc = DateTimeOffset.UtcNow;
            runtime.StateStore.Save(state);
            await runtime.ControlPlane.CompleteRunRequestAsync(new
            {
                customerId = runtime.Settings.CustomerId,
                hostId = runtime.Settings.HostId,
                runRequestId = request.RunRequestId,
                succeeded = true,
                message = "Descoberta AWS atualizada pelo Agent."
            }, ct);
        }
        catch (Exception ex)
        {
            runtime.Logger.Error("Checagem AWS solicitada pelo ControlPlane falhou.", ex);
            try
            {
                await runtime.ControlPlane.CompleteRunRequestAsync(new
                {
                    customerId = runtime.Settings.CustomerId,
                    hostId = runtime.Settings.HostId,
                    runRequestId = request.RunRequestId,
                    succeeded = false,
                    message = "Falha ao atualizar descoberta AWS. Consulte o log local do Agent."
                }, ct);
            }
            catch
            {
            }

            throw;
        }
    }

    private static async Task ReportBlockedJobAsync(
        Runtime runtime,
        ManualRunContext? manualRun,
        string failureCode,
        string failureMessage,
        CancellationToken ct)
    {
        var jobId = Guid.NewGuid().ToString("N");
        await runtime.ControlPlane.StartJobAsync(new
        {
            customerId = runtime.Settings.CustomerId,
            hostId = runtime.Settings.HostId,
            jobId,
            startedAtUtc = DateTimeOffset.UtcNow,
            runRequestId = manualRun?.RunRequestId
        }, ct);

        var finalReport = new FinalJobReportPayload
        {
            CustomerId = runtime.Settings.CustomerId,
            HostId = runtime.Settings.HostId,
            JobId = jobId,
            FinalState = "FAILED",
            PlannedBytes = 0,
            PlannedItems = 0,
            UploadedBytes = 0,
            UploadedItems = 0,
            FailureCode = failureCode,
            FailureMessage = failureMessage,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            Artifacts = Array.Empty<FinalJobArtifactPayload>(),
            RunRequestId = manualRun?.RunRequestId
        };
        await TrySendOrPersistFinalReportAsync(runtime, finalReport, ct);

        var state = runtime.StateStore.Load();
        state.LastJobId = jobId;
        state.LastFinalState = "FAILED";
        runtime.StateStore.Save(state);
    }

    private static FinalJobArtifactPayload[] BuildArtifacts(string? manifestPath, string? issueReportPath, bool dryRun)
    {
        var artifacts = new List<FinalJobArtifactPayload>();
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            artifacts.Add(new FinalJobArtifactPayload { Type = "MANIFEST_LOCAL", Location = manifestPath! });
        }

        if (!string.IsNullOrWhiteSpace(issueReportPath))
        {
            artifacts.Add(new FinalJobArtifactPayload { Type = "JOB_ISSUES_LOCAL", Location = issueReportPath! });
        }

        if (dryRun)
        {
            artifacts.Add(new FinalJobArtifactPayload { Type = "DRY_RUN", Location = "dry-run://no-upload" });
        }

        return artifacts.ToArray();
    }

    private static string? SaveIssueReportIfNeeded(string jobId, string stateDir, IReadOnlyCollection<BackupProcessingIssue> issues)
    {
        if (issues.Count == 0)
        {
            return null;
        }

        Directory.CreateDirectory(stateDir);
        var reportPath = Path.Combine(stateDir, $"issues.{jobId}.json");
        var payload = new
        {
            jobId,
            createdAtUtc = DateTimeOffset.UtcNow,
            issueCount = issues.Count,
            issues
        };

        File.WriteAllText(reportPath, JsonConvert.SerializeObject(payload, Formatting.Indented));
        return reportPath;
    }

    private static string BuildFailureCode(IReadOnlyCollection<BackupProcessingIssue> issues)
    {
        if (issues.Any(i => string.Equals(i.Code, "INCLUDE_PATH_MISSING", StringComparison.OrdinalIgnoreCase)))
        {
            return "PATH_VALIDATION_FAILED";
        }

        if (issues.Any(i => string.Equals(i.Code, "PATH_TOO_LONG", StringComparison.OrdinalIgnoreCase)))
        {
            return "LONG_PATH_NOT_SUPPORTED";
        }

        if (issues.Any(i => string.Equals(i.Code, "FILE_IN_USE", StringComparison.OrdinalIgnoreCase)))
        {
            return "FILES_IN_USE_SKIPPED";
        }

        if (issues.Any(i => string.Equals(i.Stage, "upload", StringComparison.OrdinalIgnoreCase)))
        {
            return "FILES_SKIPPED";
        }

        return "JOB_HAS_SKIPPED_ITEMS";
    }

    private static string BuildIssueSummary(IEnumerable<BackupProcessingIssue> issues)
    {
        var materialized = issues.ToArray();
        if (materialized.Length == 0)
        {
            return "Nenhum problema de path/arquivo foi detectado.";
        }

        var sample = materialized
            .Take(3)
            .Select(i => $"[{i.Code}] {i.Path}: {i.Message}")
            .ToArray();

        return $"Foram detectados {materialized.Length} problema(s) que podem comprometer a integridade do backup. Exemplos: {string.Join("; ", sample)}.";
    }

    private static BackupProcessingIssue CreateProcessingIssue(string stage, string path, string code, string message, bool isBlocking)
    {
        return new BackupProcessingIssue
        {
            Stage = stage,
            Path = path,
            Code = code,
            Message = message,
            IsBlocking = isBlocking
        };
    }

    private static string BuildIssueCode(Exception ex)
    {
        return ex switch
        {
            PathTooLongException => "PATH_TOO_LONG",
            UnauthorizedAccessException => "ACCESS_DENIED",
            IOException ioEx when IsSharingOrLockViolation(ioEx) => "FILE_IN_USE",
            FileNotFoundException => "FILE_MISSING",
            DirectoryNotFoundException => "DIRECTORY_MISSING",
            IOException => "IO_ERROR",
            _ => ex.GetType().Name.ToUpperInvariant()
        };
    }

    private static bool IsSharingOrLockViolation(IOException ex)
    {
        var win32Code = ex.HResult & 0xFFFF;
        return win32Code == 32 || win32Code == 33;
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
