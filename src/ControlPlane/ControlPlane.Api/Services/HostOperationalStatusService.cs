using ControlPlane.Api.Domain;

using DomainHost = ControlPlane.Api.Domain.Host;

namespace ControlPlane.Api.Services;

public sealed record HostOperationalStatus(
    string HeartbeatStatus,
    string Label,
    string CssClass,
    string Message,
    bool IsReadyForPolicyAssignment
);

public sealed class HostOperationalStatusService
{
    public HostOperationalStatus Evaluate(DomainHost host, AgentConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(host);

        var heartbeatStatus = GetHeartbeatStatus(host.LastHeartbeatAtUtc);
        if (string.Equals(heartbeatStatus, "Sem heartbeat", StringComparison.OrdinalIgnoreCase))
        {
            return new HostOperationalStatus(
                HeartbeatStatus: heartbeatStatus,
                Label: "Nao validado",
                CssClass: "not-ready",
                Message: "Aguardando o primeiro heartbeat do agent neste host.",
                IsReadyForPolicyAssignment: false);
        }

        if (string.Equals(heartbeatStatus, "Offline", StringComparison.OrdinalIgnoreCase))
        {
            return new HostOperationalStatus(
                HeartbeatStatus: heartbeatStatus,
                Label: "Offline",
                CssClass: "offline",
                Message: "Host sem heartbeat recente. Verifique o servico do agent e a conectividade com o ControlPlane.",
                IsReadyForPolicyAssignment: false);
        }

        if (configuration is null || !configuration.LastConfigSyncAtUtc.HasValue)
        {
            return new HostOperationalStatus(
                HeartbeatStatus: heartbeatStatus,
                Label: "Nao validado",
                CssClass: "not-ready",
                Message: "Agent online, mas ainda sem sincronizacao de configuracao e prechecks completos.",
                IsReadyForPolicyAssignment: false);
        }

        if (!IsRunningLike(configuration.ServiceStatus))
        {
            return new HostOperationalStatus(
                HeartbeatStatus: heartbeatStatus,
                Label: "Atencao",
                CssClass: "critical",
                Message: "Servico do agent nao esta em execucao normal. Verifique o estado do servico no host.",
                IsReadyForPolicyAssignment: false);
        }

        if (!configuration.PrecheckTlsOk)
        {
            return new HostOperationalStatus(
                HeartbeatStatus: heartbeatStatus,
                Label: "Atencao",
                CssClass: "critical",
                Message: ResolvePrecheckMessage(configuration, "TLS minimo nao validado no host. Verifique compatibilidade e configuracoes de seguranca."),
                IsReadyForPolicyAssignment: false);
        }

        if (!configuration.PrecheckDiskOk)
        {
            return new HostOperationalStatus(
                HeartbeatStatus: heartbeatStatus,
                Label: "Atencao",
                CssClass: "critical",
                Message: ResolvePrecheckMessage(configuration, "Precheck de disco/staging falhou. Verifique espaco, permissoes e caminho local do host."),
                IsReadyForPolicyAssignment: false);
        }

        if (!configuration.PrecheckCredentialOk)
        {
            return new HostOperationalStatus(
                HeartbeatStatus: heartbeatStatus,
                Label: "Pendente AWS",
                CssClass: "warning",
                Message: ResolvePrecheckMessage(configuration, "Verificar configuracoes AWS no Host.") +
                         " O host pode receber politica central, mas o upload real continuara bloqueado ate corrigir a credencial AWS local.",
                IsReadyForPolicyAssignment: true);
        }

        if (string.Equals(heartbeatStatus, "Atrasado", StringComparison.OrdinalIgnoreCase))
        {
            return new HostOperationalStatus(
                HeartbeatStatus: heartbeatStatus,
                Label: "Atencao",
                CssClass: "warning",
                Message: "Heartbeat atrasado. Aguarde novo heartbeat antes de vincular ou alterar a politica.",
                IsReadyForPolicyAssignment: false);
        }

        return new HostOperationalStatus(
            HeartbeatStatus: heartbeatStatus,
            Label: "Pronto",
            CssClass: "ready",
            Message: "Host validado e pronto para receber politica de backup.",
            IsReadyForPolicyAssignment: true);
    }

    public string GetHeartbeatStatus(DateTimeOffset? lastHeartbeatAtUtc)
    {
        if (lastHeartbeatAtUtc is null)
        {
            return "Sem heartbeat";
        }

        var age = DateTimeOffset.UtcNow - lastHeartbeatAtUtc.Value;
        if (age <= TimeSpan.FromMinutes(5))
        {
            return "Online";
        }

        if (age <= TimeSpan.FromMinutes(15))
        {
            return "Atrasado";
        }

        return "Offline";
    }

    private static bool IsRunningLike(string? serviceStatus)
    {
        return string.Equals(serviceStatus, "Running", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(serviceStatus, "DryRun", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePrecheckMessage(AgentConfiguration configuration, string fallback)
    {
        if (string.IsNullOrWhiteSpace(configuration.LastPrecheckMessage))
        {
            return fallback;
        }

        var message = configuration.LastPrecheckMessage.Trim();
        var policySourceIndex = message.IndexOf(" PolicySource=", StringComparison.OrdinalIgnoreCase);
        if (policySourceIndex >= 0)
        {
            message = message[..policySourceIndex].Trim();
        }

        return string.IsNullOrWhiteSpace(message) ? fallback : message;
    }
}
