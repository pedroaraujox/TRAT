using System;
using System.IO;
using System.Text.Json;

namespace WebstationBackup.Agent.Tray;

internal sealed record AgentSettingsSnapshot(
    bool Exists,
    string? ControlPlaneBaseUrl,
    string? CustomerId,
    string? HostId,
    string? AwsRegion,
    string? BucketName,
    string? BucketPrefix,
    string? ErrorMessage);

internal static class AgentSettingsReader
{
    public static AgentSettingsSnapshot Read()
    {
        var publicPath = Path.Combine(Path.GetDirectoryName(AgentPaths.SettingsFilePath)!, "agent.public.json");
        var settingsPath = File.Exists(publicPath) ? publicPath : AgentPaths.SettingsFilePath;
        if (!File.Exists(settingsPath))
        {
            return new AgentSettingsSnapshot(
                Exists: false,
                ControlPlaneBaseUrl: null,
                CustomerId: null,
                HostId: null,
                AwsRegion: null,
                BucketName: null,
                BucketPrefix: null,
                ErrorMessage: null);
        }

        try
        {
            using var stream = File.OpenRead(settingsPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            return new AgentSettingsSnapshot(
                Exists: true,
                ControlPlaneBaseUrl: GetString(root, "ControlPlaneBaseUrl"),
                CustomerId: GetString(root, "CustomerId"),
                HostId: GetString(root, "HostId"),
                AwsRegion: GetString(root, "AwsRegion"),
                BucketName: GetString(root, "S3BucketName"),
                BucketPrefix: GetString(root, "S3KeyPrefix"),
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            return new AgentSettingsSnapshot(
                Exists: true,
                ControlPlaneBaseUrl: null,
                CustomerId: null,
                HostId: null,
                AwsRegion: null,
                BucketName: null,
                BucketPrefix: null,
                ErrorMessage: $"Falha ao ler agent.settings.json: {ex.Message}");
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }
}
