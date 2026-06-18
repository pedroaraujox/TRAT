using System;
using System.IO;
using System.ServiceProcess;

namespace WebstationBackup.Agent.Tray;

internal sealed record AgentStatusSnapshot(
    string ServiceStatusLabel,
    string Summary,
    string Details,
    bool ServiceInstalled,
    bool CanOpenPanel,
    string? ControlPlaneBaseUrl,
    string SettingsStatus,
    string LastLogUpdate,
    string LastRulesUpdate,
    string CustomerId,
    string HostId,
    string AwsTarget)
{
    public static AgentStatusSnapshot Collect()
    {
        var serviceInfo = GetServiceStatus();
        var settings = AgentSettingsReader.Read();
        var lastLogUpdate = DescribeFileTimestamp(AgentPaths.LogFilePath);
        var lastRulesUpdate = DescribeFileTimestamp(AgentPaths.RulesFilePath);

        var settingsStatus = !settings.Exists
            ? "Arquivo de configuracao ainda nao encontrado."
            : settings.ErrorMessage ?? "Configuracao local encontrada.";

        var summary = serviceInfo.label switch
        {
            "Running" => "Agent ativo em segundo plano.",
            "Stopped" => "Agent instalado, mas parado.",
            "StartPending" => "Agent iniciando.",
            "StopPending" => "Agent parando.",
            "NotInstalled" => "Agent ainda nao instalado como servico.",
            _ => $"Agent em estado {serviceInfo.label}."
        };

        var details = settings.ErrorMessage
            ?? (!settings.Exists
                ? "Finalize a configuracao do agent para liberar a operacao assistida."
                : "Configuracao local carregavel e pronta para uso.");

        var controlPlaneUrl = NormalizeUrl(settings.ControlPlaneBaseUrl);
        var awsTarget = BuildAwsTarget(settings);

        return new AgentStatusSnapshot(
            ServiceStatusLabel: serviceInfo.label,
            Summary: summary,
            Details: details,
            ServiceInstalled: serviceInfo.installed,
            CanOpenPanel: !string.IsNullOrWhiteSpace(controlPlaneUrl),
            ControlPlaneBaseUrl: controlPlaneUrl,
            SettingsStatus: settingsStatus,
            LastLogUpdate: lastLogUpdate,
            LastRulesUpdate: lastRulesUpdate,
            CustomerId: settings.CustomerId ?? "Nao configurado",
            HostId: settings.HostId ?? "Nao configurado",
            AwsTarget: awsTarget);
    }

    private static (bool installed, string label) GetServiceStatus()
    {
        try
        {
            using var service = new ServiceController(AgentPaths.ServiceName);
            var status = service.Status;
            return (true, status.ToString());
        }
        catch (InvalidOperationException)
        {
            return (false, "NotInstalled");
        }
        catch
        {
            return (true, "Unknown");
        }
    }

    private static string DescribeFileTimestamp(string path)
    {
        if (!File.Exists(path))
        {
            return "Nao disponivel";
        }

        return File.GetLastWriteTime(path).ToString("dd/MM/yyyy HH:mm:ss");
    }

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.ToString();
    }

    private static string BuildAwsTarget(AgentSettingsSnapshot settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BucketName))
        {
            return "Nao configurado";
        }

        var region = string.IsNullOrWhiteSpace(settings.AwsRegion)
            ? "sem-regiao"
            : settings.AwsRegion.Trim();

        var prefix = string.IsNullOrWhiteSpace(settings.BucketPrefix)
            ? "/"
            : settings.BucketPrefix.Trim();

        return $"{settings.BucketName.Trim()} ({region}) - {prefix}";
    }
}
