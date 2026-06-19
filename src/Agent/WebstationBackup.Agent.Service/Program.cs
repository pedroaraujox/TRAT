using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using WebstationBackup.Agent.Service.Core;
using WebstationBackup.Agent.Service.Service;

namespace WebstationBackup.Agent.Service;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (!Environment.UserInteractive && !HasFlag(args, "--console"))
        {
            ServiceBase.Run(new ServiceBase[]
            {
                new BackupWindowsService()
            });
            return 0;
        }

        try
        {
            return RunConsole(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static int RunConsole(string[] args)
    {
        var dryRun = HasFlag(args, "--dry-run");
        var loop = HasFlag(args, "--loop");

        var rulesPath = GetOptionValue(args, "--rules");
        var settingsPath = GetOptionValue(args, "--settings");
        var stateDir = GetOptionValue(args, "--state-dir");

        var includeOverrides = GetMultiOptionValues(args, "--include");
        var excludeOverrides = GetMultiOptionValues(args, "--exclude");

        AgentSettings? settingsOverride = null;
        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            var loaded = AgentSettings.LoadOrThrow(Path.GetFullPath(settingsPath));
            if (includeOverrides.Count > 0 || excludeOverrides.Count > 0)
            {
                settingsOverride = new AgentSettings
                {
                    CustomerId = loaded.CustomerId,
                    HostId = loaded.HostId,
                    ControlPlaneBaseUrl = loaded.ControlPlaneBaseUrl,
                    AgentToken = loaded.AgentToken,
                    AgentTokenCredentialTargetName = loaded.AgentTokenCredentialTargetName,
                    AwsRegion = loaded.AwsRegion,
                    S3BucketName = loaded.S3BucketName,
                    S3KeyPrefix = loaded.S3KeyPrefix,
                    AwsCredentialDpapiProtected = loaded.AwsCredentialDpapiProtected,
                    AwsCredentialTargetName = loaded.AwsCredentialTargetName,
                    IncludePaths = includeOverrides.Count > 0 ? includeOverrides.ToArray() : loaded.IncludePaths,
                    ExcludePaths = excludeOverrides.Count > 0 ? excludeOverrides.ToArray() : loaded.ExcludePaths
                };
            }
        }
        else if (dryRun)
        {
            var customerId = RequireOptionValue(args, "--customer-id");
            var hostId = RequireOptionValue(args, "--host-id");
            var controlPlaneBaseUrl = RequireOptionValue(args, "--control-plane");
            var agentToken = RequireOptionValue(args, "--agent-token");
            if (includeOverrides.Count == 0)
            {
                throw new InvalidOperationException("Em modo --dry-run sem --settings, é obrigatório informar ao menos um --include.");
            }
            if (!Uri.TryCreate(controlPlaneBaseUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException("Valor inválido para --control-plane. Informe uma URL absoluta, ex.: http://localhost:5057");
            }

            settingsOverride = new AgentSettings
            {
                CustomerId = customerId,
                HostId = hostId,
                ControlPlaneBaseUrl = controlPlaneBaseUrl,
                AgentToken = agentToken,
                AwsRegion = "dry-run",
                S3BucketName = "dry-run",
                S3KeyPrefix = "dry-run",
                AwsCredentialTargetName = "dry-run",
                IncludePaths = includeOverrides.ToArray(),
                ExcludePaths = excludeOverrides.ToArray()
            };
        }
        else
        {
            throw new InvalidOperationException("Modo console sem --dry-run requer --settings <path>.");
        }

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var options = new AgentWorkerOptions
        {
            RulesPath = rulesPath,
            SettingsPath = settingsPath,
            SettingsOverride = settingsOverride,
            StateDir = stateDir,
            DryRun = dryRun
        };

        if (loop)
        {
            AgentWorker.RunLoopAsync(options, cts.Token).GetAwaiter().GetResult();
        }
        else
        {
            AgentWorker.RunOnceAsync(options, cts.Token).GetAwaiter().GetResult();
        }

        return 0;
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetOptionValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return a.Substring(name.Length + 1);
            }
            if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string RequireOptionValue(string[] args, string name)
    {
        var v = GetOptionValue(args, name);
        if (string.IsNullOrWhiteSpace(v))
        {
            throw new InvalidOperationException($"Parâmetro obrigatório ausente: {name}");
        }
        return v!.Trim();
    }

    private static List<string> GetMultiOptionValues(string[] args, string name)
    {
        var values = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                var raw = a.Substring(name.Length + 1);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    values.Add(raw.Trim());
                }
                continue;
            }

            if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var raw = args[i + 1];
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    values.Add(raw.Trim());
                }
            }
        }

        return values;
    }
}
