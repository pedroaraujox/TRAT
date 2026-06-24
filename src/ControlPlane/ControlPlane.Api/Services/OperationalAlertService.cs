using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Services;

public sealed class OperationalAlertService(
    AppDbContext db,
    HostOperationalStatusService hostOperationalStatusService,
    ILogger<OperationalAlertService> logger)
{
    private const string OperationalHealthSource = "operational_health";
    private const string JobRuntimeSource = "job_runtime";
    private static readonly TimeSpan MissingHeartbeatGracePeriod = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MissingConfigurationGracePeriod = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StaleConfigurationThreshold = TimeSpan.FromMinutes(30);

    public async Task ReconcileAllAsync(CancellationToken ct)
    {
        var hosts = await db.Hosts.AsNoTracking().ToListAsync(ct);
        var configsByHostId = (await db.AgentConfigurations.AsNoTracking().ToListAsync(ct))
            .GroupBy(c => c.HostId)
            .Select(g => g.OrderByDescending(x => x.LastConfigSyncAtUtc ?? x.CreatedAtUtc).First())
            .ToDictionary(c => c.HostId, c => c, StringComparer.OrdinalIgnoreCase);

        foreach (var host in hosts)
        {
            ct.ThrowIfCancellationRequested();
            configsByHostId.TryGetValue(host.Id, out var configuration);
            await ReconcileHostOperationalAlertsAsync(host, configuration, ct);
        }
    }

    public async Task ReconcileHostAsync(string customerId, string hostId, CancellationToken ct)
    {
        var host = await db.Hosts.AsNoTracking()
            .FirstOrDefaultAsync(h => h.CustomerId == customerId && h.Id == hostId, ct);
        if (host is null)
        {
            logger.LogDebug(
                "ReconcileHostAsync ignorado porque host nao foi encontrado. customer={CustomerId} host={HostId}",
                customerId,
                hostId);
            return;
        }

        var configuration = (await db.AgentConfigurations.AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.HostId == hostId)
            .ToListAsync(ct))
            .OrderByDescending(c => c.LastConfigSyncAtUtc ?? c.CreatedAtUtc)
            .FirstOrDefault();
        await ReconcileHostOperationalAlertsAsync(host, configuration, ct);
    }

    public async Task ObserveJobFinalStateAsync(Job job, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var unresolvedJobAlerts = await db.Alerts
            .Where(a => a.CustomerId == job.CustomerId &&
                        a.HostId == job.HostId &&
                        a.Source == JobRuntimeSource &&
                        a.ResolvedAtUtc == null)
            .ToListAsync(ct);

        if (string.Equals(job.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            ResolveAlerts(unresolvedJobAlerts, nowUtc);
            await db.SaveChangesAsync(ct);
            return;
        }

        var normalizedFailureCode = NormalizeAlertToken(job.FailureCode, fallback: "failed");
        var rootCauseKey = BuildRootCauseKey(JobRuntimeSource, job.CustomerId, job.HostId, normalizedFailureCode);

        foreach (var alert in unresolvedJobAlerts.Where(a => !string.Equals(a.RootCauseKey, rootCauseKey, StringComparison.OrdinalIgnoreCase)))
        {
            alert.ResolvedAtUtc = nowUtc;
        }

        var message = BuildJobFailureMessage(job);
            UpsertAlert(
            unresolvedJobAlerts.FirstOrDefault(a => string.Equals(a.RootCauseKey, rootCauseKey, StringComparison.OrdinalIgnoreCase)),
            new AlertDefinition(
                CustomerId: job.CustomerId,
                HostId: job.HostId,
                JobId: job.Id,
                RootCauseKey: rootCauseKey,
                Source: JobRuntimeSource,
                Type: "JOB_FAILED",
                Severity: "CRITICAL",
                Message: message,
                ObservedAtUtc: nowUtc),
            ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task ReconcileHostOperationalAlertsAsync(ControlPlane.Api.Domain.Host host, AgentConfiguration? configuration, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var expectedAlerts = BuildOperationalAlertDefinitions(host, configuration, nowUtc);
        var unresolvedAlerts = await db.Alerts
            .Where(a => a.CustomerId == host.CustomerId &&
                        a.HostId == host.Id &&
                        a.Source == OperationalHealthSource &&
                        a.ResolvedAtUtc == null)
            .ToListAsync(ct);

        var expectedKeys = expectedAlerts
            .Select(a => a.RootCauseKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var alert in unresolvedAlerts.Where(a => !expectedKeys.Contains(a.RootCauseKey)))
        {
            alert.ResolvedAtUtc = nowUtc;
        }

        foreach (var definition in expectedAlerts)
        {
            var existing = unresolvedAlerts.FirstOrDefault(a => string.Equals(a.RootCauseKey, definition.RootCauseKey, StringComparison.OrdinalIgnoreCase));
            UpsertAlert(existing, definition, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private IReadOnlyList<AlertDefinition> BuildOperationalAlertDefinitions(
        ControlPlane.Api.Domain.Host host,
        AgentConfiguration? configuration,
        DateTimeOffset nowUtc)
    {
        var definitions = new List<AlertDefinition>();
        var heartbeatStatus = hostOperationalStatusService.GetHeartbeatStatus(host.LastHeartbeatAtUtc);
        var isMissingHeartbeat = host.LastHeartbeatAtUtc is null && host.FirstSeenAtUtc <= nowUtc.Subtract(MissingHeartbeatGracePeriod);
        var isOffline = string.Equals(heartbeatStatus, "Offline", StringComparison.OrdinalIgnoreCase);
        var isDelayed = string.Equals(heartbeatStatus, "Atrasado", StringComparison.OrdinalIgnoreCase);
        var hasConfiguration = configuration is not null;
        var hasSufficientOnboardingAge = host.FirstSeenAtUtc <= nowUtc.Subtract(MissingConfigurationGracePeriod);

        if (isMissingHeartbeat)
        {
            definitions.Add(new AlertDefinition(
                host.CustomerId,
                host.Id,
                null,
                BuildRootCauseKey(OperationalHealthSource, host.CustomerId, host.Id, "heartbeat_missing"),
                OperationalHealthSource,
                "HOST_NO_HEARTBEAT",
                "WARNING",
                "O host foi cadastrado, mas ainda nao publicou heartbeat no prazo esperado. Valide servico Windows, rede corporativa e URL do ControlPlane.",
                nowUtc));

            return definitions;
        }

        if (isOffline)
        {
            definitions.Add(new AlertDefinition(
                host.CustomerId,
                host.Id,
                null,
                BuildRootCauseKey(OperationalHealthSource, host.CustomerId, host.Id, "heartbeat_offline"),
                OperationalHealthSource,
                "HOST_OFFLINE",
                "CRITICAL",
                $"O host ficou offline para o painel. Ultimo heartbeat em {host.LastHeartbeatAtUtc?.ToLocalTime():dd/MM/yyyy HH:mm:ss}.",
                nowUtc));
        }

        if (!hasConfiguration && hasSufficientOnboardingAge)
        {
            definitions.Add(new AlertDefinition(
                host.CustomerId,
                host.Id,
                null,
                BuildRootCauseKey(OperationalHealthSource, host.CustomerId, host.Id, "configuration_missing"),
                OperationalHealthSource,
                "HOST_CONFIG_MISSING",
                "WARNING",
                "O host ja enviou onboarding, mas o painel ainda nao recebeu sync de configuracao do Agent.",
                nowUtc));

            return definitions;
        }

        if (configuration is null)
        {
            return definitions;
        }

        var syncMissing = configuration.LastConfigSyncAtUtc is null;
        var syncStale = configuration.LastConfigSyncAtUtc is not null &&
                        configuration.LastConfigSyncAtUtc < nowUtc.Subtract(StaleConfigurationThreshold);
        var serviceIssue = !IsRunningLike(configuration.ServiceStatus);
        var policyMissing = string.IsNullOrWhiteSpace(configuration.PolicyId) &&
                            string.IsNullOrWhiteSpace(configuration.EffectivePolicyId);

        if (!isOffline && syncMissing && hasSufficientOnboardingAge)
        {
            definitions.Add(new AlertDefinition(
                host.CustomerId,
                host.Id,
                null,
                BuildRootCauseKey(OperationalHealthSource, host.CustomerId, host.Id, "configuration_sync_missing"),
                OperationalHealthSource,
                "HOST_CONFIG_SYNC_MISSING",
                "WARNING",
                "O Agent ainda nao publicou uma sincronizacao completa de configuracao para este host.",
                nowUtc));
        }

        if (!isOffline && !syncMissing && syncStale)
        {
            definitions.Add(new AlertDefinition(
                host.CustomerId,
                host.Id,
                null,
                BuildRootCauseKey(OperationalHealthSource, host.CustomerId, host.Id, "configuration_sync_stale"),
                OperationalHealthSource,
                "HOST_CONFIG_SYNC_STALE",
                isDelayed ? "WARNING" : "CRITICAL",
                $"A ultima sync de configuracao esta desatualizada desde {configuration.LastConfigSyncAtUtc?.ToLocalTime():dd/MM/yyyy HH:mm:ss}.",
                nowUtc));
        }

        if (serviceIssue)
        {
            definitions.Add(new AlertDefinition(
                host.CustomerId,
                host.Id,
                null,
                BuildRootCauseKey(OperationalHealthSource, host.CustomerId, host.Id, "service_not_running"),
                OperationalHealthSource,
                "HOST_SERVICE_NOT_RUNNING",
                "CRITICAL",
                $"O Agent reportou ServiceStatus={configuration.ServiceStatus}. Revise servico Windows, update parcial ou instalacao corrompida.",
                nowUtc));
        }

        if (!configuration.PrecheckTlsOk)
        {
            definitions.Add(new AlertDefinition(
                host.CustomerId,
                host.Id,
                null,
                BuildRootCauseKey(OperationalHealthSource, host.CustomerId, host.Id, "precheck_tls_failed"),
                OperationalHealthSource,
                "HOST_TLS_FAILED",
                "CRITICAL",
                BuildPrecheckMessage(
                    configuration,
                    fallback: "O precheck TLS falhou. Revise certificados, proxy corporativo, inspeccao SSL e TLS minimo."),
                nowUtc));
        }

        if (!configuration.PrecheckDiskOk)
        {
            definitions.Add(new AlertDefinition(
                host.CustomerId,
                host.Id,
                null,
                BuildRootCauseKey(OperationalHealthSource, host.CustomerId, host.Id, "precheck_disk_failed"),
                OperationalHealthSource,
                "HOST_STAGING_FAILED",
                "CRITICAL",
                BuildPrecheckMessage(
                    configuration,
                    fallback: $"O staging local {configuration.StagingPath} nao passou na validacao de disco ou permissao."),
                nowUtc));
        }

        if (!configuration.PrecheckCredentialOk)
        {
            definitions.Add(new AlertDefinition(
                host.CustomerId,
                host.Id,
                null,
                BuildRootCauseKey(OperationalHealthSource, host.CustomerId, host.Id, "precheck_credential_failed"),
                OperationalHealthSource,
                "HOST_AWS_CREDENTIAL_FAILED",
                "WARNING",
                BuildPrecheckMessage(
                    configuration,
                    fallback: $"A credencial local {configuration.CredentialTargetName} nao esta valida para upload."),
                nowUtc));
        }

        if (!serviceIssue &&
            configuration.PrecheckTlsOk &&
            configuration.PrecheckDiskOk &&
            !syncMissing &&
            !syncStale &&
            policyMissing)
        {
            definitions.Add(new AlertDefinition(
                host.CustomerId,
                host.Id,
                null,
                BuildRootCauseKey(OperationalHealthSource, host.CustomerId, host.Id, "policy_missing"),
                OperationalHealthSource,
                "HOST_POLICY_MISSING",
                "WARNING",
                "O host esta operacional, mas ainda nao possui politica efetiva vinculada para agenda de backup.",
                nowUtc));
        }

        return definitions;
    }

    private void UpsertAlert(Alert? existing, AlertDefinition definition, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (existing is null)
        {
            db.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid().ToString("N"),
                CustomerId = definition.CustomerId,
                HostId = definition.HostId,
                JobId = definition.JobId,
                RootCauseKey = definition.RootCauseKey,
                Source = definition.Source,
                Type = definition.Type,
                Severity = definition.Severity,
                Message = definition.Message,
                CreatedAtUtc = definition.ObservedAtUtc,
                LastObservedAtUtc = definition.ObservedAtUtc,
                AcknowledgedAtUtc = null,
                ResolvedAtUtc = null
            });
            return;
        }

        existing.JobId = definition.JobId;
        existing.Type = definition.Type;
        existing.Severity = definition.Severity;
        existing.Message = definition.Message;
        existing.Source = definition.Source;
        existing.RootCauseKey = definition.RootCauseKey;
        existing.LastObservedAtUtc = definition.ObservedAtUtc;
        existing.ResolvedAtUtc = null;
    }

    private static void ResolveAlerts(IEnumerable<Alert> alerts, DateTimeOffset resolvedAtUtc)
    {
        foreach (var alert in alerts.Where(a => a.ResolvedAtUtc == null))
        {
            alert.ResolvedAtUtc = resolvedAtUtc;
        }
    }

    private static string BuildRootCauseKey(string source, string customerId, string hostId, string causeCode)
    {
        return $"{NormalizeAlertToken(source, "source")}:{NormalizeAlertToken(customerId, "customer")}:{NormalizeAlertToken(hostId, "host")}:{NormalizeAlertToken(causeCode, "cause")}";
    }

    private static string NormalizeAlertToken(string? rawValue, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        var buffer = rawValue
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var normalized = new string(buffer);
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        normalized = normalized.Trim('-');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return fallback;
        }

        return normalized.Length > 40 ? normalized[..40] : normalized;
    }

    private static bool IsRunningLike(string? serviceStatus)
    {
        return string.Equals(serviceStatus, "Running", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(serviceStatus, "DryRun", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPrecheckMessage(AgentConfiguration configuration, string fallback)
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

    private static string BuildJobFailureMessage(Job job)
    {
        var failureCode = string.IsNullOrWhiteSpace(job.FailureCode) ? "FAILED" : job.FailureCode.Trim();
        var failureMessage = string.IsNullOrWhiteSpace(job.FailureMessage) ? "Job finalizado com falha." : job.FailureMessage.Trim();
        return $"{failureCode}: {failureMessage}";
    }

    private sealed record AlertDefinition(
        string CustomerId,
        string HostId,
        string? JobId,
        string RootCauseKey,
        string Source,
        string Type,
        string Severity,
        string Message,
        DateTimeOffset ObservedAtUtc);
}
