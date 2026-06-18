using System;
using System.Text.Json.Serialization;

namespace WebstationBackup.Agent.Installer;

internal sealed class InstallerSettingsModel
{
    public string CustomerId { get; set; } = string.Empty;
    public string HostId { get; set; } = string.Empty;
    public string ControlPlaneBaseUrl { get; set; } = string.Empty;
    public string AgentToken { get; set; } = string.Empty;
    public string? AgentTokenDpapiProtected { get; set; }
    public string? AgentTokenCredentialTargetName { get; set; }
    public string? AwsRegion { get; set; }
    public string? S3BucketName { get; set; }
    public string? S3KeyPrefix { get; set; }
    public string? AwsCredentialTargetName { get; set; }

    [JsonPropertyName("IncludePaths")]
    public string[] IncludePaths { get; set; } = Array.Empty<string>();

    [JsonPropertyName("ExcludePaths")]
    public string[] ExcludePaths { get; set; } = Array.Empty<string>();
}
