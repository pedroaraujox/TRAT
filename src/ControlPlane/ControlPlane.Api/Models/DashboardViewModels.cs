namespace ControlPlane.Api.Models;

public sealed class DashboardViewModel
{
    public required DashboardSummary Summary { get; init; }
    public required IReadOnlyList<CustomerCardViewModel> Customers { get; init; }
    public required IReadOnlyList<HostRowViewModel> Hosts { get; init; }
    public required IReadOnlyList<JobRowViewModel> RecentJobs { get; init; }
    public required IReadOnlyList<AlertRowViewModel> Alerts { get; init; }
}

public sealed class DashboardSummary
{
    public int CustomerCount { get; init; }
    public int HostCount { get; init; }
    public int ActiveAlertCount { get; init; }
    public int FailedJobsLast7Days { get; init; }
}

public sealed class CustomerCardViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string AwsAccountId { get; init; }
    public int HostCount { get; init; }
    public int FailedJobsLast30Days { get; init; }
    public DateTimeOffset? LastJobAtUtc { get; init; }
}

public sealed class HostRowViewModel
{
    public required string Id { get; init; }
    public required string CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? ConfigurationId { get; init; }
    public required string Hostname { get; init; }
    public required string OsVersion { get; init; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; init; }
    public string HeartbeatStatus { get; init; } = "Unknown";
    public string? ServiceStatus { get; init; }
    public string? AssignedPolicyName { get; init; }
    public bool? PrecheckTlsOk { get; init; }
    public bool? PrecheckDiskOk { get; init; }
    public bool? PrecheckCredentialOk { get; init; }
}

public sealed class JobRowViewModel
{
    public required string Id { get; init; }
    public required string CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public required string HostId { get; init; }
    public string? Hostname { get; init; }
    public required string State { get; init; }
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; init; }
    public long PlannedBytes { get; init; }
    public long UploadedBytes { get; init; }
    public long PlannedItems { get; init; }
    public long UploadedItems { get; init; }
    public string? FailureCode { get; init; }
    public string? FailureMessage { get; init; }
}

public sealed class AlertRowViewModel
{
    public required string Id { get; init; }
    public required string CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? HostId { get; init; }
    public string? Hostname { get; init; }
    public string? JobId { get; init; }
    public required string Type { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? AcknowledgedAtUtc { get; init; }
}

public sealed class CustomersPageViewModel
{
    public required string? Search { get; init; }
    public required IReadOnlyList<CustomerListItemViewModel> Customers { get; init; }
}

public sealed class CustomerListItemViewModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string AwsAccountId { get; init; }
    public string? NotificationEmailsCsv { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public int HostCount { get; init; }
    public int JobCount { get; init; }
    public int ActiveAlertCount { get; init; }
}

public sealed class CustomerDetailViewModel
{
    public required CustomerFormViewModel Customer { get; init; }
    public required IReadOnlyList<HostRowViewModel> Hosts { get; init; }
    public required IReadOnlyList<JobRowViewModel> Jobs { get; init; }
    public required IReadOnlyList<AlertRowViewModel> Alerts { get; init; }
    public required IReadOnlyList<PolicyListItemViewModel> Policies { get; init; }
}

public sealed record CustomerFormViewModel
{
    public string? OriginalId { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string AwsAccountId { get; init; }
    public string? NotificationEmailsCsv { get; init; }
    public bool IsEditMode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class HostsPageViewModel
{
    public string? CustomerId { get; init; }
    public string? Status { get; init; }
    public required IReadOnlyList<HostRowViewModel> Hosts { get; init; }
}

public sealed record HostFormViewModel
{
    public string? OriginalId { get; init; }
    public required string Id { get; init; }
    public required string CustomerId { get; init; }
    public required string Hostname { get; init; }
    public required string OsVersion { get; init; }
    public bool IsEditMode { get; init; }
    public DateTimeOffset? FirstSeenAtUtc { get; init; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; init; }
    public string? ConfigurationId { get; init; }
    public string? AssignedPolicyName { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class JobsPageViewModel
{
    public string? CustomerId { get; init; }
    public string? HostId { get; init; }
    public string? State { get; init; }
    public required IReadOnlyList<JobRowViewModel> Jobs { get; init; }
}

public sealed class JobDetailViewModel
{
    public required JobRowViewModel Job { get; init; }
    public required IReadOnlyList<ArtifactRowViewModel> Artifacts { get; init; }
    public required IReadOnlyList<AlertRowViewModel> Alerts { get; init; }
}

public sealed class ArtifactRowViewModel
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Location { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class AlertsPageViewModel
{
    public string? CustomerId { get; init; }
    public string? Severity { get; init; }
    public string? Status { get; init; }
    public required IReadOnlyList<AlertRowViewModel> Alerts { get; init; }
}

public sealed class PoliciesPageViewModel
{
    public string? CustomerId { get; init; }
    public string? ScopeType { get; init; }
    public required IReadOnlyList<PolicyListItemViewModel> Policies { get; init; }
}

public sealed class PolicyListItemViewModel
{
    public required string Id { get; init; }
    public required string CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? HostId { get; init; }
    public string? Hostname { get; init; }
    public required string Name { get; init; }
    public required string ScopeType { get; init; }
    public required string IncludePathsCsv { get; init; }
    public string? ExcludePathsCsv { get; init; }
    public required string ScheduleDaysCsv { get; init; }
    public required string StartTimeLocal { get; init; }
    public int MaxRuntimeMinutes { get; init; }
    public int CpuLimitPercent { get; init; }
    public int NetworkLimitMbit { get; init; }
    public bool Enabled { get; init; }
}

public sealed record PolicyFormViewModel
{
    public string? OriginalId { get; init; }
    public required string Id { get; init; }
    public required string CustomerId { get; init; }
    public string? HostId { get; init; }
    public required string Name { get; init; }
    public required string ScopeType { get; init; }
    public required string IncludePathsCsv { get; init; }
    public string? ExcludePathsCsv { get; init; }
    public required string ScheduleDaysCsv { get; init; }
    public required string StartTimeLocal { get; init; }
    public int MaxRuntimeMinutes { get; init; }
    public int CpuLimitPercent { get; init; }
    public int NetworkLimitMbit { get; init; }
    public bool Enabled { get; init; }
    public bool IsEditMode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class AgentConfigurationsPageViewModel
{
    public string? CustomerId { get; init; }
    public string? ServiceStatus { get; init; }
    public required IReadOnlyList<AgentConfigurationListItemViewModel> Configurations { get; init; }
}

public sealed class AgentConfigurationListItemViewModel
{
    public required string Id { get; init; }
    public required string CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public required string HostId { get; init; }
    public string? Hostname { get; init; }
    public string? PolicyId { get; init; }
    public string? PolicyName { get; init; }
    public required string AgentVersion { get; init; }
    public required string ServiceStatus { get; init; }
    public required string TlsMode { get; init; }
    public bool PrecheckTlsOk { get; init; }
    public bool PrecheckDiskOk { get; init; }
    public bool PrecheckCredentialOk { get; init; }
    public required string StagingPath { get; init; }
    public required string CredentialTargetName { get; init; }
    public required string UploadMode { get; init; }
    public DateTimeOffset? LastConfigSyncAtUtc { get; init; }
    public DateTimeOffset? LastPrecheckAtUtc { get; init; }
    public string? LastPrecheckMessage { get; init; }
}

public sealed class AgentConfigurationDetailViewModel
{
    public required AgentConfigurationListItemViewModel Configuration { get; init; }
    public required IReadOnlyList<JobRowViewModel> RecentJobs { get; init; }
    public required IReadOnlyList<PolicyOptionViewModel> PolicyOptions { get; init; }
}

public sealed class PolicyOptionViewModel
{
    public required string Id { get; init; }
    public required string Label { get; init; }
}

public sealed class LoginViewModel
{
    public string? ErrorMessage { get; init; }
}
