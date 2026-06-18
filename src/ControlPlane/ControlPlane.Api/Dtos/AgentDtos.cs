namespace ControlPlane.Api.Dtos;

public sealed record AgentHeartbeatRequest(
    string CustomerId,
    string HostId,
    string Hostname,
    string OsVersion,
    DateTimeOffset TimestampUtc
);

public sealed record JobStartRequest(
    string CustomerId,
    string HostId,
    string JobId,
    DateTimeOffset StartedAtUtc
);

public sealed record JobProgressReport(
    string CustomerId,
    string HostId,
    string JobId,
    string State,
    long PlannedBytes,
    long PlannedItems,
    long UploadedBytes,
    long UploadedItems,
    DateTimeOffset TimestampUtc
);

public sealed record JobFinalReport(
    string CustomerId,
    string HostId,
    string JobId,
    string FinalState,
    long PlannedBytes,
    long PlannedItems,
    long UploadedBytes,
    long UploadedItems,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset FinishedAtUtc,
    IReadOnlyList<JobArtifactDto> Artifacts
);

public sealed record JobArtifactDto(
    string Type,
    string Location
);

public sealed record AgentConfigurationReportRequest(
    string CustomerId,
    string HostId,
    string? EffectivePolicyId,
    string? EffectivePolicyName,
    string? EffectivePolicyKind,
    string? EffectivePolicySource,
    DateTimeOffset? EffectivePolicyLastChangedAtUtc,
    string AgentVersion,
    string ServiceStatus,
    string TlsMode,
    bool PrecheckTlsOk,
    bool PrecheckDiskOk,
    bool PrecheckCredentialOk,
    string StagingPath,
    string CredentialTargetName,
    string UploadMode,
    DateTimeOffset TimestampUtc,
    DateTimeOffset? PrecheckAtUtc,
    string? PrecheckMessage
);

public sealed record AgentEffectivePolicyResponse(
    string CustomerId,
    string HostId,
    bool Resolved,
    string ResolutionSource,
    string? PolicyId,
    string? PolicyName,
    string? PolicyKind,
    string? PolicyScopeType,
    DateTimeOffset? PolicyLastChangedAtUtc,
    string[] IncludePaths,
    string[] ExcludePaths,
    string[] ScheduleDaysOfWeek,
    string ScheduleStartTimeLocal,
    int MaxRuntimeMinutes,
    int CpuLimitPercent,
    int NetworkLimitMbit
);
