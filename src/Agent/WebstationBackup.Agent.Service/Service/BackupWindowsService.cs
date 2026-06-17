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

    protected override void OnStart(string[] args)
    {
        ServiceName = "WebstationBackupAgent";
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
    public static async Task RunLoopAsync(AgentWorkerOptions options, CancellationToken ct)
    {
        var runtime = BootstrapOrThrow(options);

        while (!ct.IsCancellationRequested)
        {
            var nextRunDelay = TimeSpan.FromMinutes(1);
            try
            {
                await SendHeartbeatAsync(runtime.Settings, runtime.ControlPlane, ct);
                await ReportConfigurationIfDueAsync(runtime, options.DryRun, ct);

                var eval = runtime.Schedule.Evaluate(runtime.Rules, DateTimeOffset.UtcNow);
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

                await ExecuteOneJobAsync(runtime, options.DryRun, ct);
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
        await SendHeartbeatAsync(runtime.Settings, runtime.ControlPlane, ct);
        await ReportConfigurationAsync(runtime, options.DryRun, ct);
        await ExecuteOneJobAsync(runtime, options.DryRun, ct);
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

    private static async Task ReportConfigurationIfDueAsync(Runtime runtime, bool dryRun, CancellationToken ct)
    {
        var state = runtime.StateStore.Load();
        if (state.LastConfigReportAtUtc.HasValue && DateTimeOffset.UtcNow - state.LastConfigReportAtUtc.Value < TimeSpan.FromMinutes(15))
        {
            return;
        }

        await ReportConfigurationAsync(runtime, dryRun, ct);
        state.LastConfigReportAtUtc = DateTimeOffset.UtcNow;
        runtime.StateStore.Save(state);
    }

    private static async Task ReportConfigurationAsync(Runtime runtime, bool dryRun, CancellationToken ct)
    {
        var rules = runtime.Rules;
        var settings = runtime.Settings;
        var logger = runtime.Logger;
        var controlPlane = runtime.ControlPlane;

        var agentVersion = GetAgentVersion();
        var serviceStatus = dryRun ? "DryRun" : "Running";
        var tlsMode = "TLS1.2";

        var now = DateTimeOffset.UtcNow;

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
        if (!dryRun)
        {
            try
            {
                _ = WindowsCredentialManager.ReadAwsKeysOrThrow(settings.AwsCredentialTargetName);
            }
            catch (Exception ex)
            {
                credOk = false;
                precheckOk = false;
                precheckMessages.Add("Falha ao ler credenciais AWS: " + ex.GetType().Name);
            }
        }

        var message = precheckMessages.Count == 0 ? (precheckOk ? "Prechecks OK." : "Prechecks falharam.") : string.Join(" ", precheckMessages);

        await controlPlane.ReportConfigurationAsync(new
        {
            customerId = settings.CustomerId,
            hostId = settings.HostId,
            agentVersion,
            serviceStatus,
            tlsMode,
            precheckTlsOk = tlsOk,
            precheckDiskOk = diskOk,
            precheckCredentialOk = credOk,
            stagingPath,
            credentialTargetName = settings.AwsCredentialTargetName,
            uploadMode = dryRun ? "dry-run" : "direct-s3",
            timestampUtc = now,
            precheckAtUtc = now,
            precheckMessage = message
        }, ct);

        logger.Info("Config report enviado", new Dictionary<string, object?>
        {
            ["serviceStatus"] = serviceStatus,
            ["tlsOk"] = tlsOk,
            ["diskOk"] = diskOk,
            ["credOk"] = credOk,
            ["stagingPath"] = stagingPath
        });
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

    private static async Task ExecuteOneJobAsync(Runtime runtime, bool dryRun, CancellationToken ct)
    {
        var rules = runtime.Rules;
        var settings = runtime.Settings;
        var logger = runtime.Logger;
        var controlPlane = runtime.ControlPlane;
        var stateStore = runtime.StateStore;

        if (!rules.SecurityAndSafety.Aws.NeverDeleteFromS3)
        {
            throw new InvalidOperationException("Config inválida: neverDeleteFromS3 precisa ser true.");
        }

        PrecheckOrThrow(rules, settings);

        var jobId = Guid.NewGuid().ToString("N");
        await controlPlane.StartJobAsync(new
        {
            customerId = settings.CustomerId,
            hostId = settings.HostId,
            jobId,
            startedAtUtc = DateTimeOffset.UtcNow
        }, ct);

        var scanner = new FileScanner();
        var files = scanner.ScanFiles(settings.IncludePaths, settings.ExcludePaths);

        var builder = new ManifestBuilder();
        var manifest = builder.Build(jobId, settings, rules, files);

        var manifestPath = builder.SaveToFile(manifest, runtime.StateDir);
        logger.Info("Manifest gerado", new Dictionary<string, object?>
        {
            ["jobId"] = jobId,
            ["plannedBytes"] = manifest.PlannedBytes,
            ["plannedItems"] = manifest.PlannedItems,
            ["dryRun"] = dryRun
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

        long uploadedBytes = 0;
        long uploadedItems = 0;

        if (dryRun)
        {
            uploadedBytes = manifest.PlannedBytes;
            uploadedItems = manifest.PlannedItems;
        }
        else
        {
            var keys = WindowsCredentialManager.ReadAwsKeysOrThrow(settings.AwsCredentialTargetName);
            var uploader = new S3Uploader(settings.AwsRegion, settings.S3BucketName, settings.S3KeyPrefix, keys.AccessKeyId, keys.SecretAccessKey, logger);

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

        var finalState = "SUCCEEDED";
        string? failureCode = null;
        string? failureMessage = null;

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

        await controlPlane.ReportFinalAsync(new
        {
            customerId = settings.CustomerId,
            hostId = settings.HostId,
            jobId,
            finalState,
            plannedBytes = manifest.PlannedBytes,
            plannedItems = manifest.PlannedItems,
            uploadedBytes,
            uploadedItems,
            failureCode,
            failureMessage,
            finishedAtUtc = DateTimeOffset.UtcNow,
            artifacts = dryRun
                ? new[] { new { type = "MANIFEST_LOCAL", location = manifestPath }, new { type = "DRY_RUN", location = "dry-run://no-upload" } }
                : new[] { new { type = "MANIFEST_LOCAL", location = manifestPath } }
        }, ct);

        var state = stateStore.Load();
        state.LastJobId = jobId;
        state.LastFinalState = finalState;
        stateStore.Save(state);
    }
}
