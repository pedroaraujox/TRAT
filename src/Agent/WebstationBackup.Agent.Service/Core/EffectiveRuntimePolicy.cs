using System;
using System.Linq;

namespace WebstationBackup.Agent.Service.Core;

internal sealed class RemoteEffectivePolicyResponse
{
    public string CustomerId { get; set; } = string.Empty;
    public string HostId { get; set; } = string.Empty;
    public bool Resolved { get; set; }
    public string ResolutionSource { get; set; } = string.Empty;
    public string? PolicyId { get; set; }
    public string? PolicyName { get; set; }
    public string? PolicyKind { get; set; }
    public string? PolicyScopeType { get; set; }
    public DateTimeOffset? PolicyLastChangedAtUtc { get; set; }
    public string[] IncludePaths { get; set; } = Array.Empty<string>();
    public string[] ExcludePaths { get; set; } = Array.Empty<string>();
    public string[] ScheduleDaysOfWeek { get; set; } = Array.Empty<string>();
    public string ScheduleStartTimeLocal { get; set; } = string.Empty;
    public int MaxRuntimeMinutes { get; set; }
    public int CpuLimitPercent { get; set; }
    public int NetworkLimitMbit { get; set; }
}

internal sealed class EffectiveRuntimePolicy
{
    public required string Source { get; init; }
    public string? PolicyId { get; init; }
    public string? PolicyName { get; init; }
    public string? PolicyKind { get; init; }
    public DateTimeOffset? PolicyLastChangedAtUtc { get; init; }
    public string[] IncludePaths { get; init; } = Array.Empty<string>();
    public string[] ExcludePaths { get; init; } = Array.Empty<string>();
    public string[] ScheduleDaysOfWeek { get; init; } = Array.Empty<string>();
    public required string ScheduleStartTimeLocal { get; init; }
    public int MaxRuntimeMinutes { get; init; }
    public int CpuLimitPercent { get; init; }
    public int NetworkLimitMbit { get; init; }

    public static EffectiveRuntimePolicy FromLocal(ProjectRules rules, AgentSettings settings)
    {
        return new EffectiveRuntimePolicy
        {
            Source = "local_fallback",
            PolicyId = null,
            PolicyName = null,
            PolicyKind = "local_fallback",
            PolicyLastChangedAtUtc = null,
            IncludePaths = NormalizePaths(settings.IncludePaths),
            ExcludePaths = NormalizePaths(settings.ExcludePaths),
            ScheduleDaysOfWeek = NormalizeTokens(rules.Defaults.Schedule.DaysOfWeek),
            ScheduleStartTimeLocal = NormalizeTime(rules.Defaults.Schedule.StartTimeLocal, "22:00"),
            MaxRuntimeMinutes = NormalizePositive(rules.Defaults.Schedule.MaxRuntimeMinutes, 720),
            CpuLimitPercent = NormalizePositive(rules.Defaults.Throttling.CpuMaxPercent, 35),
            NetworkLimitMbit = NormalizePositive(rules.Defaults.Throttling.NetworkMaxMbit, 80)
        };
    }

    public static EffectiveRuntimePolicy FromRemote(ProjectRules rules, AgentSettings settings, RemoteEffectivePolicyResponse remote)
    {
        var local = FromLocal(rules, settings);
        var normalizedIncludePaths = NormalizePaths(remote.IncludePaths);
        var normalizedScheduleDays = NormalizeTokens(remote.ScheduleDaysOfWeek);
        var normalizedPolicyId = string.IsNullOrWhiteSpace(remote.PolicyId) ? null : remote.PolicyId?.Trim();
        return new EffectiveRuntimePolicy
        {
            Source = string.IsNullOrWhiteSpace(remote.ResolutionSource) ? "remote_effective_policy" : remote.ResolutionSource.Trim(),
            PolicyId = normalizedPolicyId,
            PolicyName = string.IsNullOrWhiteSpace(remote.PolicyName) ? null : remote.PolicyName!.Trim(),
            PolicyKind = NormalizePolicyKind(remote.PolicyKind),
            PolicyLastChangedAtUtc = remote.PolicyLastChangedAtUtc,
            IncludePaths = normalizedIncludePaths.Length > 0 ? normalizedIncludePaths : local.IncludePaths,
            ExcludePaths = NormalizePaths(remote.ExcludePaths),
            ScheduleDaysOfWeek = normalizedScheduleDays.Length > 0 ? normalizedScheduleDays : local.ScheduleDaysOfWeek,
            ScheduleStartTimeLocal = NormalizeTime(remote.ScheduleStartTimeLocal, local.ScheduleStartTimeLocal),
            MaxRuntimeMinutes = NormalizePositive(remote.MaxRuntimeMinutes, local.MaxRuntimeMinutes),
            CpuLimitPercent = NormalizePositive(remote.CpuLimitPercent, local.CpuLimitPercent),
            NetworkLimitMbit = NormalizePositive(remote.NetworkLimitMbit, local.NetworkLimitMbit)
        };
    }

    private static string[] NormalizePaths(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] NormalizeTokens(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeTime(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmedValue = value!.Trim();
        var parts = trimmedValue.Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var hour) ||
            !int.TryParse(parts[1], out var minute))
        {
            return fallback;
        }

        hour = Math.Max(0, Math.Min(23, hour));
        minute = Math.Max(0, Math.Min(59, minute));
        return string.Format("{0:00}:{1:00}", hour, minute);
    }

    private static int NormalizePositive(int value, int fallback)
    {
        return value > 0 ? value : fallback;
    }

    private static string NormalizePolicyKind(string? value)
    {
        if (string.Equals(value, "bootstrap", StringComparison.OrdinalIgnoreCase))
        {
            return "bootstrap";
        }

        if (string.Equals(value, "operational", StringComparison.OrdinalIgnoreCase))
        {
            return "operational";
        }

        return "operational";
    }

    public string BuildFingerprint()
    {
        var changedAt = PolicyLastChangedAtUtc?.ToUniversalTime().ToString("O") ?? "-";
        return string.Join("|",
            Source ?? string.Empty,
            PolicyId ?? string.Empty,
            PolicyName ?? string.Empty,
            PolicyKind ?? string.Empty,
            changedAt,
            string.Join(";", IncludePaths ?? Array.Empty<string>()),
            string.Join(";", ExcludePaths ?? Array.Empty<string>()),
            string.Join(",", ScheduleDaysOfWeek ?? Array.Empty<string>()),
            ScheduleStartTimeLocal ?? string.Empty,
            MaxRuntimeMinutes.ToString(),
            CpuLimitPercent.ToString(),
            NetworkLimitMbit.ToString());
    }
}
