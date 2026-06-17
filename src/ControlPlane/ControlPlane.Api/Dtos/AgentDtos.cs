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
