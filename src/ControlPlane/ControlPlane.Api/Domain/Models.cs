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
    public string? JobId { get; set; }

    [MaxLength(160)]
    public required string RootCauseKey { get; set; }

    [MaxLength(32)]
    public required string Source { get; set; }

    [MaxLength(32)]
    public required string Type { get; set; }

    [MaxLength(16)]
    public required string Severity { get; set; }

    [MaxLength(2000)]
    public required string Message { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastObservedAtUtc { get; set; }

    public DateTimeOffset? AcknowledgedAtUtc { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; set; }
}

public sealed class AuditEvent
{
    [Key]
    [MaxLength(64)]
    public required string Id { get; init; }

    [MaxLength(32)]
    public required string Category { get; init; }

    [MaxLength(32)]
    public required string Action { get; init; }

    [MaxLength(16)]
    public required string Outcome { get; init; }

    [MaxLength(64)]
    public required string EntityType { get; init; }

    [MaxLength(64)]
    public string? EntityId { get; init; }

    [MaxLength(64)]
    public string? CustomerId { get; init; }

    [MaxLength(64)]
    public string? HostId { get; init; }

    [MaxLength(64)]
    public string? ActorUserId { get; init; }

    [MaxLength(320)]
    public string? ActorEmail { get; init; }

    [MaxLength(160)]
    public string? ActorDisplayName { get; init; }

    [MaxLength(256)]
    public string? Route { get; init; }

    [MaxLength(64)]
    public string? IpAddress { get; init; }

    [MaxLength(512)]
    public string? UserAgent { get; init; }

    [MaxLength(2000)]
    public required string Message { get; init; }

    [MaxLength(4000)]
    public string? MetadataJson { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
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

    [MaxLength(32)]
    public required string PolicyKind { get; set; }

    [MaxLength(16)]
    public required string ScopeType { get; set; }

    [MaxLength(64)]
    public string? HostId { get; set; }

    [MaxLength(64)]
    public string? OriginHostId { get; set; }

    [MaxLength(2000)]
    public required string IncludePathsCsv { get; set; }

    [MaxLength(2000)]
    public string? ExcludePathsCsv { get; set; }

    [MaxLength(64)]
    public string? AwsRegion { get; set; }

    [MaxLength(128)]
    public string? S3BucketName { get; set; }

    [MaxLength(1024)]
    public string? S3KeyPrefix { get; set; }

    [MaxLength(64)]
    public required string ScheduleDaysCsv { get; set; }

    [MaxLength(8)]
    public required string StartTimeLocal { get; set; }

    public int MaxRuntimeMinutes { get; set; }

    public int CpuLimitPercent { get; set; }

    public int NetworkLimitMbit { get; set; }

    public bool Enabled { get; set; }

    public DateTimeOffset? LastChangedAtUtc { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class AgentRunRequest
{
    [Key]
    [MaxLength(64)]
    public required string Id { get; init; }

    [MaxLength(64)]
    public required string CustomerId { get; init; }

    [MaxLength(64)]
    public required string HostId { get; init; }

    [MaxLength(32)]
    public required string TriggerType { get; set; }

    [MaxLength(32)]
    public required string State { get; set; }

    [MaxLength(128)]
    public required string RequestedBy { get; init; }

    public required DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ClaimedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    [MaxLength(64)]
    public string? JobId { get; set; }

    [MaxLength(2000)]
    public string? FailureMessage { get; set; }
}

public sealed class PolicyChangeEvent
{
    [Key]
    [MaxLength(64)]
    public required string Id { get; init; }

    [MaxLength(64)]
    public required string PolicyId { get; init; }

    [MaxLength(32)]
    public required string EventType { get; init; }

    [MaxLength(2000)]
    public required string Message { get; init; }

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
    public string? EffectivePolicyId { get; set; }

    [MaxLength(128)]
    public string? EffectivePolicyName { get; set; }

    [MaxLength(32)]
    public string? EffectivePolicyKind { get; set; }

    [MaxLength(64)]
    public string? EffectivePolicySource { get; set; }

    public DateTimeOffset? EffectivePolicyLastChangedAtUtc { get; set; }

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

public sealed class PanelUser
{
    [Key]
    [MaxLength(64)]
    public required string Id { get; init; }

    [MaxLength(320)]
    public required string Email { get; set; }

    [MaxLength(160)]
    public required string DisplayName { get; set; }

    [MaxLength(24)]
    public required string Role { get; set; }

    [MaxLength(512)]
    public required string PasswordHash { get; set; }

    [MaxLength(256)]
    public required string PasswordSalt { get; set; }

    public int PasswordIterations { get; set; }

    public bool IsActive { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LastFailedLoginAtUtc { get; set; }

    public DateTimeOffset? LockoutUntilUtc { get; set; }

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
