using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Services;

public sealed class PolicyResolutionService(AppDbContext db, ILogger<PolicyResolutionService> logger)
{
    public async Task<AgentEffectivePolicyResponse> ResolveEffectivePolicyAsync(string customerId, string hostId, CancellationToken ct)
    {
        var normalizedCustomerId = NormalizeRequired(customerId, nameof(customerId));
        var normalizedHostId = NormalizeRequired(hostId, nameof(hostId));

        var resolution = await ResolvePolicyEntityAsync(normalizedCustomerId, normalizedHostId, ct);
        if (resolution.Policy is null)
        {
            return new AgentEffectivePolicyResponse(
                normalizedCustomerId,
                normalizedHostId,
                Resolved: false,
                ResolutionSource: resolution.Source,
                PolicyId: null,
                PolicyName: null,
                PolicyKind: null,
                PolicyScopeType: null,
                PolicyLastChangedAtUtc: null,
                IncludePaths: Array.Empty<string>(),
                ExcludePaths: Array.Empty<string>(),
                AwsRegion: null,
                S3BucketName: null,
                S3KeyPrefix: null,
                ScheduleDaysOfWeek: Array.Empty<string>(),
                ScheduleStartTimeLocal: string.Empty,
                MaxRuntimeMinutes: 0,
                CpuLimitPercent: 0,
                NetworkLimitMbit: 0
            );
        }

        var includePaths = SplitPathList(resolution.Policy.IncludePathsCsv);
        if (includePaths.Length == 0)
        {
            logger.LogWarning(
                "Resolved policy {PolicyId} for customer={CustomerId} host={HostId} was ignored because it has no include paths.",
                resolution.Policy.Id,
                normalizedCustomerId,
                normalizedHostId);

            return new AgentEffectivePolicyResponse(
                normalizedCustomerId,
                normalizedHostId,
                Resolved: false,
                ResolutionSource: "invalid_policy_include_paths",
                PolicyId: null,
                PolicyName: null,
                PolicyKind: null,
                PolicyScopeType: null,
                PolicyLastChangedAtUtc: null,
                IncludePaths: Array.Empty<string>(),
                ExcludePaths: Array.Empty<string>(),
                AwsRegion: null,
                S3BucketName: null,
                S3KeyPrefix: null,
                ScheduleDaysOfWeek: Array.Empty<string>(),
                ScheduleStartTimeLocal: string.Empty,
                MaxRuntimeMinutes: 0,
                CpuLimitPercent: 0,
                NetworkLimitMbit: 0
            );
        }

        return new AgentEffectivePolicyResponse(
            normalizedCustomerId,
            normalizedHostId,
            Resolved: true,
            ResolutionSource: resolution.Source,
            PolicyId: resolution.Policy.Id,
            PolicyName: resolution.Policy.Name,
            PolicyKind: NormalizePolicyKind(resolution.Policy.PolicyKind),
            PolicyScopeType: resolution.Policy.ScopeType,
            PolicyLastChangedAtUtc: resolution.Policy.LastChangedAtUtc ?? resolution.Policy.CreatedAtUtc,
            IncludePaths: includePaths,
            ExcludePaths: SplitPathList(resolution.Policy.ExcludePathsCsv),
            AwsRegion: NormalizeOptional(resolution.Policy.AwsRegion),
            S3BucketName: NormalizeOptional(resolution.Policy.S3BucketName),
            S3KeyPrefix: NormalizeOptional(resolution.Policy.S3KeyPrefix),
            ScheduleDaysOfWeek: SplitTokenList(resolution.Policy.ScheduleDaysCsv),
            ScheduleStartTimeLocal: NormalizeScheduleTime(resolution.Policy.StartTimeLocal),
            MaxRuntimeMinutes: Clamp(resolution.Policy.MaxRuntimeMinutes, 1, 7 * 24 * 60, 720),
            CpuLimitPercent: Clamp(resolution.Policy.CpuLimitPercent, 1, 100, 35),
            NetworkLimitMbit: Clamp(resolution.Policy.NetworkLimitMbit, 1, 100_000, 80)
        );
    }

    private async Task<(BackupPolicy? Policy, string Source)> ResolvePolicyEntityAsync(string customerId, string hostId, CancellationToken ct)
    {
        var latestConfiguration = (await db.AgentConfigurations.AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.HostId == hostId)
            .ToListAsync(ct))
            .OrderByDescending(c => c.LastConfigSyncAtUtc ?? c.CreatedAtUtc)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(latestConfiguration?.PolicyId))
        {
            var assignedPolicy = await db.BackupPolicies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == latestConfiguration.PolicyId, ct);
            if (IsPolicyApplicable(assignedPolicy, customerId, hostId))
            {
                return (assignedPolicy, "configuration_assignment");
            }

            logger.LogWarning(
                "Assigned policy {PolicyId} for customer={CustomerId} host={HostId} is not applicable and will be ignored.",
                latestConfiguration.PolicyId,
                customerId,
                hostId);
        }

        var candidatePolicies = await db.BackupPolicies.AsNoTracking()
            .Where(p => p.CustomerId == customerId && p.Enabled)
            .ToListAsync(ct);

        var hostPolicy = candidatePolicies
            .Where(p => string.Equals(p.ScopeType, "host", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.HostId, hostId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (hostPolicy is not null)
        {
            return (hostPolicy, "host_scope_fallback");
        }

        var customerPolicy = candidatePolicies
            .Where(p => string.Equals(p.ScopeType, "customer", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (customerPolicy is not null)
        {
            return (customerPolicy, "customer_scope_fallback");
        }

        return (null, "none");
    }

    private static bool IsPolicyApplicable(BackupPolicy? policy, string customerId, string hostId)
    {
        if (policy is null || !policy.Enabled)
        {
            return false;
        }

        if (!string.Equals(policy.CustomerId, customerId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(policy.ScopeType, "customer", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(policy.ScopeType, "host", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(policy.HostId, hostId, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRequired(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} é obrigatório.", name);
        }

        return value.Trim();
    }

    private static string[] SplitPathList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] SplitTokenList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToUpperInvariant())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeScheduleTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "22:00";
        }

        var parts = value.Trim().Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var hour) ||
            !int.TryParse(parts[1], out var minute))
        {
            return "22:00";
        }

        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        return $"{hour:00}:{minute:00}";
    }

    private static int Clamp(int value, int min, int max, int fallback)
    {
        if (value < min || value > max)
        {
            return fallback;
        }

        return value;
    }

    private static string NormalizePolicyKind(string? value)
    {
        return string.Equals(value, "bootstrap", StringComparison.OrdinalIgnoreCase)
            ? "bootstrap"
            : "operational";
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
