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
    public required string OperationalStatusLabel { get; init; }
    public required string OperationalStatusCssClass { get; init; }
    public required string OperationalStatusMessage { get; init; }
    public bool IsReadyForPolicyAssignment { get; init; }
    public required string BootstrapStatusLabel { get; init; }
    public required string BootstrapStatusCssClass { get; init; }
    public required string BootstrapStatusMessage { get; init; }
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
    public string? EnrollmentTokenOneTime { get; init; }
    public required AwsIntegrationViewModel AwsIntegration { get; init; }
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
    public string? OperationalStatusLabel { get; init; }
    public string? OperationalStatusCssClass { get; init; }
    public string? OperationalStatusMessage { get; init; }
    public string? BootstrapIncludePathsCsv { get; init; }
    public string? BootstrapExcludePathsCsv { get; init; }
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
    public required string PolicyKind { get; init; }
    public string? OriginHostId { get; init; }
    public required string ScopeType { get; init; }
    public required string IncludePathsCsv { get; init; }
    public string? ExcludePathsCsv { get; init; }
    public string? AwsRegion { get; init; }
    public string? S3BucketName { get; init; }
    public string? S3KeyPrefix { get; init; }
    public required string ScheduleDaysCsv { get; init; }
    public required string StartTimeLocal { get; init; }
    public int MaxRuntimeMinutes { get; init; }
    public int CpuLimitPercent { get; init; }
    public int NetworkLimitMbit { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset? LastChangedAtUtc { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record PolicyFormViewModel
{
    public string? OriginalId { get; init; }
    public required string Id { get; init; }
    public required string CustomerId { get; init; }
    public string? HostId { get; init; }
    public required string Name { get; init; }
    public required string PolicyKind { get; init; }
    public string? OriginHostId { get; init; }
    public required string ScopeType { get; init; }
    public required string IncludePathsCsv { get; init; }
    public string? ExcludePathsCsv { get; init; }
    public string? AwsRegion { get; init; }
    public string? S3BucketName { get; init; }
    public string? S3KeyPrefix { get; init; }
    public required string ScheduleDaysCsv { get; init; }
    public IReadOnlyList<string> ScheduleDays { get; init; } = Array.Empty<string>();
    public required string StartTimeLocal { get; init; }
    public int MaxRuntimeMinutes { get; init; }
    public int CpuLimitPercent { get; init; }
    public int NetworkLimitMbit { get; init; }
    public bool Enabled { get; init; }
    public bool IsEditMode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? LastChangedAtUtc { get; init; }
    public IReadOnlyList<PolicyChangeEventViewModel> RecentEvents { get; init; } = Array.Empty<PolicyChangeEventViewModel>();
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
    public string? EffectivePolicyId { get; init; }
    public string? EffectivePolicyName { get; init; }
    public string? EffectivePolicyKind { get; init; }
    public string? EffectivePolicySource { get; init; }
    public DateTimeOffset? EffectivePolicyLastChangedAtUtc { get; init; }
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
    public required string OperationalStatusLabel { get; init; }
    public required string OperationalStatusCssClass { get; init; }
    public required string OperationalStatusMessage { get; init; }
    public bool IsReadyForPolicyAssignment { get; init; }
    public string? BootstrapIncludePathsCsv { get; init; }
    public string? BootstrapExcludePathsCsv { get; init; }
    public required string BootstrapStatusLabel { get; init; }
    public required string BootstrapStatusCssClass { get; init; }
    public required string BootstrapStatusMessage { get; init; }
    public bool BootstrapPolicyExists { get; init; }
    public bool IsBootstrapPolicyAssigned { get; init; }
    public string? BootstrapPolicyId { get; init; }
}

public sealed class AgentConfigurationDetailViewModel
{
    public required AgentConfigurationListItemViewModel Configuration { get; init; }
    public required AwsIntegrationViewModel AwsIntegration { get; init; }
    public HostRunRequestViewModel? LatestRunRequest { get; init; }
    public required IReadOnlyList<JobRowViewModel> RecentJobs { get; init; }
    public required IReadOnlyList<PolicyOptionViewModel> PolicyOptions { get; init; }
    public required BootstrapPolicyDraftViewModel BootstrapPolicyDraft { get; init; }
}

public sealed class PolicyOptionViewModel
{
    public required string Id { get; init; }
    public required string Label { get; init; }
}

public sealed record BootstrapPolicyDraftViewModel
{
    public required string SuggestedPolicyId { get; init; }
    public required string Name { get; init; }
    public required string ScopeType { get; init; }
    public required string CustomerId { get; init; }
    public required string HostId { get; init; }
    public required string IncludePathsCsv { get; init; }
    public string? ExcludePathsCsv { get; init; }
    public string? AwsRegion { get; init; }
    public string? S3BucketName { get; init; }
    public string? S3KeyPrefix { get; init; }
    public required string ScheduleDaysCsv { get; init; }
    public IReadOnlyList<string> ScheduleDays { get; init; } = Array.Empty<string>();
    public required string StartTimeLocal { get; init; }
    public int MaxRuntimeMinutes { get; init; }
    public int CpuLimitPercent { get; init; }
    public int NetworkLimitMbit { get; init; }
    public bool Enabled { get; init; }
    public bool HasBootstrapPaths { get; init; }
}

public sealed class AwsIntegrationViewModel
{
    public bool IsConnected { get; init; }
    public string? ExpectedAccountId { get; init; }
    public string? ResolvedAccountId { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? SelectedBucket { get; init; }
    public string? SelectedBucketRegion { get; init; }
    public string? CurrentPrefix { get; set; }
    public IReadOnlyList<string> Buckets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Prefixes { get; init; } = Array.Empty<string>();
}

public sealed class HostRunRequestViewModel
{
    public required string Id { get; init; }
    public required string State { get; init; }
    public required string TriggerType { get; init; }
    public DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset? ClaimedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public string? JobId { get; init; }
    public string? FailureMessage { get; init; }
}

public sealed record PolicyChangeEventViewModel
{
    public required string EventType { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class LoginViewModel
{
    public string? Email { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class PanelAdministrationViewModel
{
    public required PanelCurrentUserViewModel CurrentUser { get; init; }
    public required AgentDownloadViewModel AgentDownload { get; init; }
    public int UserCount { get; init; }
    public int ActiveUserCount { get; init; }
    public int AdminUserCount { get; init; }
}

public sealed class PanelUsersPageViewModel
{
    public required PanelCurrentUserViewModel CurrentUser { get; init; }
    public required IReadOnlyList<PanelUserListItemViewModel> Users { get; init; }
}

public sealed record PanelUserListItemViewModel
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public DateTimeOffset? LastLoginAtUtc { get; init; }
}

public sealed record PanelUserFormViewModel
{
    public string? OriginalId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
    public bool IsActive { get; init; }
    public string? Password { get; init; }
    public string? ConfirmPassword { get; init; }
    public bool IsEditMode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record PanelCurrentUserViewModel
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
    public bool IsAdmin { get; init; }
}

public sealed record AgentDownloadViewModel
{
    public bool IsAvailable { get; init; }
    public required string Message { get; init; }
    public required string SearchRoot { get; init; }
    public string? Version { get; init; }
    public DateTimeOffset? PublishedAtUtc { get; init; }
    public bool HasSetupExe { get; init; }
    public bool HasZip { get; init; }
}
