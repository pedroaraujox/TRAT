using System;
using System.IO;
using Newtonsoft.Json;
using WebstationBackup.Agent.Service.Security;

namespace WebstationBackup.Agent.Service.Core;

internal sealed class AgentSettings
{
    public required string CustomerId { get; init; }
    public required string HostId { get; init; }
    public required string ControlPlaneBaseUrl { get; init; }
    public required string AgentToken { get; init; }
    public string? AgentTokenDpapiProtected { get; init; }
    public string? AgentTokenCredentialTargetName { get; init; }
    public string? AwsRegion { get; init; }
    public string? S3BucketName { get; init; }
    public string? S3KeyPrefix { get; init; }
    public string? AwsCredentialTargetName { get; init; }
    public string[] IncludePaths { get; init; } = Array.Empty<string>();
    public string[] ExcludePaths { get; init; } = Array.Empty<string>();

    public static AgentSettings LoadOrThrow(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Arquivo de configuração do Agent não encontrado.", path);
        }

        var raw = File.ReadAllText(path);
        var settings = JsonConvert.DeserializeObject<AgentSettings>(raw);
        if (settings is null)
        {
            throw new InvalidOperationException("Falha ao desserializar AgentSettings.");
        }

        Validate(settings);
        var resolvedToken = ResolveTokenOrThrow(settings);
        if (string.Equals(resolvedToken, settings.AgentToken, StringComparison.Ordinal))
        {
            return settings;
        }

        return new AgentSettings
        {
            CustomerId = settings.CustomerId,
            HostId = settings.HostId,
            ControlPlaneBaseUrl = settings.ControlPlaneBaseUrl,
            AgentToken = resolvedToken,
            AgentTokenDpapiProtected = settings.AgentTokenDpapiProtected,
            AgentTokenCredentialTargetName = settings.AgentTokenCredentialTargetName,
            AwsRegion = settings.AwsRegion,
            S3BucketName = settings.S3BucketName,
            S3KeyPrefix = settings.S3KeyPrefix,
            AwsCredentialTargetName = settings.AwsCredentialTargetName,
            IncludePaths = settings.IncludePaths ?? Array.Empty<string>(),
            ExcludePaths = settings.ExcludePaths ?? Array.Empty<string>()
        };
    }

    private static void Validate(AgentSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.CustomerId)) throw new InvalidOperationException("CustomerId é obrigatório.");
        if (string.IsNullOrWhiteSpace(s.HostId)) throw new InvalidOperationException("HostId é obrigatório.");
        if (string.IsNullOrWhiteSpace(s.ControlPlaneBaseUrl)) throw new InvalidOperationException("ControlPlaneBaseUrl é obrigatório.");
        if (string.IsNullOrWhiteSpace(s.AgentToken) &&
            string.IsNullOrWhiteSpace(s.AgentTokenDpapiProtected) &&
            string.IsNullOrWhiteSpace(s.AgentTokenCredentialTargetName))
        {
            throw new InvalidOperationException("AgentToken é obrigatório (ou AgentTokenDpapiProtected, ou AgentTokenCredentialTargetName).");
        }
    }

    public static string BuildDefaultAgentTokenTarget(string customerId, string hostId)
    {
        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(hostId))
        {
            return "WebstationBackupAgentToken";
        }

        return $"WebstationBackupAgentToken-{customerId.Trim()}-{hostId.Trim()}";
    }

    private static string ResolveTokenOrThrow(AgentSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.AgentToken))
        {
            return s.AgentToken.Trim();
        }

        if (!string.IsNullOrWhiteSpace(s.AgentTokenDpapiProtected))
        {
            return DpapiSecretProtector.UnprotectBase64OrThrow(s.AgentTokenDpapiProtected!.Trim());
        }

        var target = string.IsNullOrWhiteSpace(s.AgentTokenCredentialTargetName)
            ? BuildDefaultAgentTokenTarget(s.CustomerId, s.HostId)
            : s.AgentTokenCredentialTargetName!.Trim();

        return WindowsCredentialManager.ReadGenericSecretOrThrow(target);
    }
}
