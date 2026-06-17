using System;
using System.IO;
using Newtonsoft.Json;

namespace WebstationBackup.Agent.Service.Core;

internal sealed class AgentSettings
{
    public required string CustomerId { get; init; }
    public required string HostId { get; init; }
    public required string ControlPlaneBaseUrl { get; init; }
    public required string AgentToken { get; init; }
    public required string AwsRegion { get; init; }
    public required string S3BucketName { get; init; }
    public required string S3KeyPrefix { get; init; }
    public required string AwsCredentialTargetName { get; init; }
    public required string[] IncludePaths { get; init; }
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
        return settings;
    }

    private static void Validate(AgentSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.CustomerId)) throw new InvalidOperationException("CustomerId é obrigatório.");
        if (string.IsNullOrWhiteSpace(s.HostId)) throw new InvalidOperationException("HostId é obrigatório.");
        if (string.IsNullOrWhiteSpace(s.ControlPlaneBaseUrl)) throw new InvalidOperationException("ControlPlaneBaseUrl é obrigatório.");
        if (string.IsNullOrWhiteSpace(s.AgentToken)) throw new InvalidOperationException("AgentToken é obrigatório.");
        if (string.IsNullOrWhiteSpace(s.AwsRegion)) throw new InvalidOperationException("AwsRegion é obrigatório.");
        if (string.IsNullOrWhiteSpace(s.S3BucketName)) throw new InvalidOperationException("S3BucketName é obrigatório.");
        if (string.IsNullOrWhiteSpace(s.S3KeyPrefix)) throw new InvalidOperationException("S3KeyPrefix é obrigatório.");
        if (string.IsNullOrWhiteSpace(s.AwsCredentialTargetName)) throw new InvalidOperationException("AwsCredentialTargetName é obrigatório.");
        if (s.IncludePaths is null || s.IncludePaths.Length == 0) throw new InvalidOperationException("IncludePaths deve ter ao menos um path.");
    }
}
