using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebstationBackup.Agent.Installer;

internal static class InstallerSettingsWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Write(string outputPath, InstallerSettingsModel model)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(model);

        var normalized = new InstallerSettingsModel
        {
            CustomerId = model.CustomerId.Trim(),
            HostId = model.HostId.Trim(),
            ControlPlaneBaseUrl = model.ControlPlaneBaseUrl.Trim().TrimEnd('/'),
            AgentToken = model.AgentToken.Trim(),
            AgentTokenDpapiProtected = string.IsNullOrWhiteSpace(model.AgentTokenDpapiProtected) ? null : model.AgentTokenDpapiProtected.Trim(),
            AgentTokenCredentialTargetName = string.IsNullOrWhiteSpace(model.AgentTokenCredentialTargetName) ? null : model.AgentTokenCredentialTargetName.Trim(),
            AwsRegion = string.IsNullOrWhiteSpace(model.AwsRegion) ? null : model.AwsRegion.Trim(),
            S3BucketName = string.IsNullOrWhiteSpace(model.S3BucketName) ? null : model.S3BucketName.Trim(),
            S3KeyPrefix = string.IsNullOrWhiteSpace(model.S3KeyPrefix) ? null : NormalizePrefix(model.S3KeyPrefix),
            AwsCredentialDpapiProtected = string.IsNullOrWhiteSpace(model.AwsCredentialDpapiProtected) ? null : model.AwsCredentialDpapiProtected.Trim(),
            AwsCredentialTargetName = string.IsNullOrWhiteSpace(model.AwsCredentialTargetName) ? null : model.AwsCredentialTargetName.Trim(),
            IncludePaths = model.IncludePaths,
            ExcludePaths = model.ExcludePaths
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))
            ?? throw new InvalidOperationException("Nao foi possivel determinar a pasta de saida do settings.");

        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(outputPath, json + Environment.NewLine);
    }

    private static string NormalizePrefix(string prefix)
    {
        var trimmed = prefix.Trim().Replace('\\', '/');
        return trimmed.TrimStart('/');
    }
}
