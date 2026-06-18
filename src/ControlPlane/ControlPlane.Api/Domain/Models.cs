using System.ComponentModel.DataAnnotations;

namespace ControlPlane.Api.Domain;

public sealed class Customer
{
    [Key]
    [MaxLength(64)]
    public required string Id { get; init; }

    [MaxLength(200)]
    public required string Name { get; set; }

    [MaxLength(32)]
    public required string AwsAccountId { get; set; }

    [MaxLength(2000)]
    public string? NotificationEmailsCsv { get; set; }

    [MaxLength(64)]
    public string? AgentEnrollmentTokenHash { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class Host
{
    [Key]
    [MaxLength(64)]
    public required string Id { get; init; }

    [MaxLength(64)]
    public required string CustomerId { get; init; }

    [MaxLength(255)]
    public required string Hostname { get; set; }

    [MaxLength(64)]
    public required string OsVersion { get; set; }

    [MaxLength(4000)]
    public string? BootstrapIncludePathsCsv { get; set; }

    [MaxLength(4000)]
    public string? BootstrapExcludePathsCsv { get; set; }

    public required DateTimeOffset FirstSeenAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }
}

public sealed class Job
{
    [Key]
    [MaxLength(64)]
    public required string Id { get; init; }

    [MaxLength(64)]
    public required string CustomerId { get; init; }

    [MaxLength(64)]
    public required string HostId { get; init; }

    [MaxLength(32)]
    public required string State { get; set; }

    public required DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public long PlannedBytes { get; set; }

    public long PlannedItems { get; set; }

    public long UploadedBytes { get; set; }

    public long UploadedItems { get; set; }

    [MaxLength(64)]
    public string? FailureCode { get; set; }

    [MaxLength(2000)]
    public string? FailureMessage { get; set; }
}

public sealed class Artifact
{
    [Key]
    [MaxLength(64)]
    public required string Id { get; init; }

    [MaxLength(64)]
    public required string JobId { get; init; }

    [MaxLength(32)]
    public required string Type { get; init; }

    [MaxLength(2048)]
    public required string Location { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class Alert
{
    [Key]
    [MaxLength(64)]
    public required string Id { get; init; }

    [MaxLength(64)]
    public required string CustomerId { get; init; }

    [MaxLength(64)]
    public string? HostId { get; init; }

    [MaxLength(64)]
    public string? JobId { get; init; }

    [MaxLength(32)]
    public required string Type { get; init; }

    [MaxLength(16)]
    public required string Severity { get; init; }

    [MaxLength(2000)]
    public required string Message { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
}

public sealed class BackupPolicy
{
    [Key]
    [MaxLength(64)]
    public required string Id { get; init; }

    [MaxLength(64)]
    public required string CustomerId { get; set; }

    [MaxLength(128)]
    public required string Name { get; set; }

    [MaxLength(16)]
    public required string ScopeType { get; set; }

    [MaxLength(64)]
    public string? HostId { get; set; }

    [MaxLength(2000)]
    public required string IncludePathsCsv { get; set; }

    [MaxLength(2000)]
    public string? ExcludePathsCsv { get; set; }

    [MaxLength(64)]
    public required string ScheduleDaysCsv { get; set; }

    [MaxLength(8)]
    public required string StartTimeLocal { get; set; }

    public int MaxRuntimeMinutes { get; set; }

    public int CpuLimitPercent { get; set; }

    public int NetworkLimitMbit { get; set; }

    public bool Enabled { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class AgentConfiguration
{
    [Key]
    [MaxLength(64)]
    public required string Id { get; init; }

    [MaxLength(64)]
    public required string CustomerId { get; init; }

    [MaxLength(64)]
    public required string HostId { get; init; }

    [MaxLength(64)]
    public string? PolicyId { get; set; }

    [MaxLength(64)]
    public required string AgentVersion { get; set; }

    [MaxLength(32)]
    public required string ServiceStatus { get; set; }

    [MaxLength(64)]
    public required string TlsMode { get; set; }

    public bool PrecheckTlsOk { get; set; }

    public bool PrecheckDiskOk { get; set; }

    public bool PrecheckCredentialOk { get; set; }

    [MaxLength(1024)]
    public required string StagingPath { get; set; }

    [MaxLength(128)]
    public required string CredentialTargetName { get; set; }

    [MaxLength(32)]
    public required string UploadMode { get; set; }

    public DateTimeOffset? LastConfigSyncAtUtc { get; set; }

    public DateTimeOffset? LastPrecheckAtUtc { get; set; }

    [MaxLength(2000)]
    public string? LastPrecheckMessage { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
