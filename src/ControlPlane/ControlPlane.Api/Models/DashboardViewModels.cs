namespace ControlPlane.Api.Models;

public sealed class DashboardViewModel
{
    public required DashboardSummary Summary { get; init; }
    public required DashboardHealthSummary Health { get; init; }
    public required AlertAnalyticsSummaryViewModel AlertAnalytics { get; init; }
    public required IReadOnlyList<CustomerCardViewModel> Customers { get; init; }
    public required IReadOnlyList<HostRowViewModel> Hosts { get; init; }
    public required IReadOnlyList<JobRowViewModel> RecentJobs { get; init; }
    public required IReadOnlyList<AlertRowViewModel> Alerts { get; init; }
    public required IReadOnlyList<OperationalRiskItemViewModel> OperationalRisks { get; init; }
    public required IReadOnlyList<AuditEventRowViewModel> RecentAuditEvents { get; init; }
}

public sealed class DashboardSummary
{
    public int CustomerCount { get; init; }
    public int HostCount { get; init; }
    public int ActiveAlertCount { get; init; }
    public int FailedJobsLast7Days { get; init; }
    public int SuccessfulJobsLast24Hours { get; init; }
}

public sealed class DashboardHealthSummary
{
    public int OfflineHostCount { get; init; }
    public int CredentialFailureHostCount { get; init; }
    public int StaleConfigurationCount { get; init; }
    public int PendingManualRunCount { get; init; }
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
    public string? AgentVersion { get; init; }
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
    public required string RecoveryStatusLabel { get; init; }
    public required string RecoveryStatusCssClass { get; init; }
    public required string RecoveryStatusMessage { get; init; }
    public string? LastJobState { get; init; }
    public DateTimeOffset? LastJobAtUtc { get; init; }
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

    public bool IsActive =>
        string.Equals(State, "STARTED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(State, "PRECHECK", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(State, "SCANNING", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(State, "UPLOADING", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(State, "VERIFYING", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(State, "FINALIZING", StringComparison.OrdinalIgnoreCase);

    public int ProgressPercent
    {
        get
        {
            if (string.Equals(State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            if (PlannedBytes > 0)
            {
                return ToPercent(UploadedBytes, PlannedBytes);
            }

            if (PlannedItems > 0)
            {
                return ToPercent(UploadedItems, PlannedItems);
            }

            return IsActive ? 5 : 0;
        }
    }

    private static int ToPercent(long current, long total)
    {
        if (total <= 0)
        {
            return 0;
        }

        if (current <= 0)
        {
            return 0;
        }

        if (current >= total)
        {
            return 100;
        }

        var percent = (int)((current * 100L) / total);
        return Math.Clamp(percent, 0, 100);
    }
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
    public string? RecoveryStatusLabel { get; init; }
    public string? RecoveryStatusCssClass { get; init; }
    public string? RecoveryStatusMessage { get; init; }
    public string? SuggestedActionText { get; init; }
    public string? SuggestedActionUrl { get; init; }
    public string? Source { get; init; }
    public string? RootCauseKey { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset LastObservedAtUtc { get; init; }
    public DateTimeOffset? AcknowledgedAtUtc { get; init; }
    public DateTimeOffset? ResolvedAtUtc { get; init; }
}

public sealed class OperationalRiskItemViewModel
{
    public required string Title { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? LinkText { get; init; }
    public string? LinkUrl { get; init; }
}

public sealed class AuditEventRowViewModel
{
    public required string Category { get; init; }
    public required string Action { get; init; }
    public required string Outcome { get; init; }
    public required string EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? ActorDisplayName { get; init; }
    public string? ActorEmail { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
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
    public required AlertAnalyticsSummaryViewModel AlertAnalytics { get; init; }
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
    public string? RecoveryStatus { get; init; }
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
    public string? AgentVersion { get; init; }
    public string? ServiceStatus { get; init; }
    public string? AssignedPolicyName { get; init; }
    public string? OperationalStatusLabel { get; init; }
    public string? OperationalStatusCssClass { get; init; }
    public string? OperationalStatusMessage { get; init; }
    public string? BootstrapIncludePathsCsv { get; init; }
    public string? BootstrapExcludePathsCsv { get; init; }
    public HostOperationalHealthViewModel? OperationalHealth { get; init; }
    public AlertAnalyticsSummaryViewModel? AlertAnalytics { get; init; }
    public AwsIntegrationViewModel? AwsIntegration { get; init; }
    public BootstrapPolicyDraftViewModel? BootstrapPolicyDraft { get; init; }
    public HostRunRequestViewModel? LatestRunRequest { get; init; }
    public IReadOnlyList<JobRowViewModel> RecentJobs { get; init; } = Array.Empty<JobRowViewModel>();
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
    public string? RecoveryStatus { get; init; }
    public required AlertAnalyticsSummaryViewModel Analytics { get; init; }
    public required IReadOnlyList<AlertRowViewModel> Alerts { get; init; }
}

public sealed class AlertAnalyticsSummaryViewModel
{
    public int OpenAlertCount { get; init; }
    public int AcknowledgedAlertCount { get; init; }
    public int ResolvedAlertCount { get; init; }
    public int DistinctRootCauseCount { get; init; }
    public int ReincidentRootCauseCount { get; init; }
    public string? AverageTimeToResolveLabel { get; init; }
    public string? LongestOpenDurationLabel { get; init; }
    public required IReadOnlyList<AlertTimelineItemViewModel> Timeline { get; init; }
    public required IReadOnlyList<AlertCauseAnalyticsViewModel> TopRootCauses { get; init; }
}

public sealed class AlertTimelineItemViewModel
{
    public required string Title { get; init; }
    public required string Severity { get; init; }
    public required string StatusLabel { get; init; }
    public required string StatusCssClass { get; init; }
    public required string Message { get; init; }
    public string? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? HostId { get; init; }
    public string? Hostname { get; init; }
    public string? LinkText { get; init; }
    public string? LinkUrl { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
}

public sealed class AlertCauseAnalyticsViewModel
{
    public required string RootCauseKey { get; init; }
    public required string Title { get; init; }
    public required string Severity { get; init; }
    public int OccurrenceCount { get; init; }
    public int OpenCount { get; init; }
    public int ResolvedCount { get; init; }
    public required string AverageTimeToResolveLabel { get; init; }
    public string? LatestHostId { get; init; }
    public string? LatestHostname { get; init; }
    public DateTimeOffset LatestObservedAtUtc { get; init; }
    public string? LinkText { get; init; }
    public string? LinkUrl { get; init; }
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
    public string[] ScheduleDays { get; init; } = Array.Empty<string>();
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
    public string? RecoveryStatus { get; init; }
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
    public required string RecoveryStatusLabel { get; init; }
    public required string RecoveryStatusCssClass { get; init; }
    public required string RecoveryStatusMessage { get; init; }
}

public sealed class AgentConfigurationDetailViewModel
{
    public required AgentConfigurationListItemViewModel Configuration { get; init; }
    public required HostOperationalHealthViewModel OperationalHealth { get; init; }
    public required AwsIntegrationViewModel AwsIntegration { get; init; }
    public HostRunRequestViewModel? LatestRunRequest { get; init; }
    public required IReadOnlyList<JobRowViewModel> RecentJobs { get; init; }
    public required IReadOnlyList<PolicyOptionViewModel> PolicyOptions { get; init; }
    public required BootstrapPolicyDraftViewModel BootstrapPolicyDraft { get; init; }
}

public sealed class HostOperationalHealthViewModel
{
    public required string RiskLevelLabel { get; init; }
    public required string RiskLevelCssClass { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<OperationalHealthSignalViewModel> Signals { get; init; }
    public required IReadOnlyList<OperationalRecoveryStepViewModel> RecoverySteps { get; init; }
}

public sealed class OperationalHealthSignalViewModel
{
    public required string Name { get; init; }
    public required string StatusLabel { get; init; }
    public required string StatusCssClass { get; init; }
    public required string Detail { get; init; }
}

public sealed class OperationalRecoveryStepViewModel
{
    public required string Severity { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string? LinkText { get; init; }
    public string? LinkUrl { get; init; }
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
    public string[] ScheduleDays { get; init; } = Array.Empty<string>();
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
    public string? EnvironmentName { get; init; }
    public string? ControlPlaneBaseUrl { get; init; }
}
