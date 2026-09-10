using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
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
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), WindowsIdentity.GetCurrent().User! })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        if (File.Exists(outputPath)) new FileInfo(outputPath).SetAccessControl(security);
        using var stream = new FileInfo(outputPath).Create(FileMode.Create, FileSystemRights.FullControl,
            FileShare.None, 4096, FileOptions.None, security);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(json);
        File.WriteAllText(Path.Combine(directory, "agent.public.json"), JsonSerializer.Serialize(new {
            normalized.ControlPlaneBaseUrl, normalized.CustomerId, normalized.HostId,
            normalized.AwsRegion, normalized.S3BucketName, normalized.S3KeyPrefix
        }, JsonOptions));
    }

    private static string NormalizePrefix(string prefix)
    {
        var trimmed = prefix.Trim().Replace('\\', '/');
        return trimmed.TrimStart('/');
    }
}
