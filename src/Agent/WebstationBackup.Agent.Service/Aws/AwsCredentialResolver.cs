using System;
using System.Collections.Generic;
using Amazon.Runtime;
using WebstationBackup.Agent.Service.Security;

namespace WebstationBackup.Agent.Service.Aws;

internal sealed class ResolvedAwsCredentials
{
    public ResolvedAwsCredentials(AWSCredentials credentials, string source, string reference)
    {
        Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim();
        Reference = string.IsNullOrWhiteSpace(reference) ? "N/A" : reference.Trim();
    }

    public AWSCredentials Credentials { get; }
    public string Source { get; }
    public string Reference { get; }
}

internal static class AwsCredentialResolver
{
    public static ResolvedAwsCredentials ResolveOrThrow(string? targetName, string? dpapiProtectedCredential)
    {
        var failures = new List<string>(capacity: 3);

        if (!string.IsNullOrWhiteSpace(dpapiProtectedCredential))
        {
            try
            {
                var keys = ReadProtectedAwsKeysOrThrow(dpapiProtectedCredential!.Trim());
                return new ResolvedAwsCredentials(
                    new BasicAWSCredentials(keys.AccessKeyId, keys.SecretAccessKey),
                    source: "DpapiLocalMachine",
                    reference: "agent.settings.json");
            }
            catch (Exception ex)
            {
                failures.Add("DpapiLocalMachine: " + ex.GetType().Name);
            }
        }

        if (!string.IsNullOrWhiteSpace(targetName))
        {
            var normalizedTargetName = targetName!.Trim();
            try
            {
                var keys = WindowsCredentialManager.ReadAwsKeysOrThrow(normalizedTargetName);
                return new ResolvedAwsCredentials(
                    new BasicAWSCredentials(keys.AccessKeyId, keys.SecretAccessKey),
                    source: "WindowsCredentialManager",
                    reference: normalizedTargetName);
            }
            catch (Exception ex)
            {
                failures.Add("WindowsCredentialManager: " + ex.GetType().Name);
            }
        }

        try
        {
            var credentials = FallbackCredentialsFactory.GetCredentials();
            var immutable = credentials.GetCredentials();
            if (string.IsNullOrWhiteSpace(immutable.AccessKey) || string.IsNullOrWhiteSpace(immutable.SecretKey))
            {
                throw new InvalidOperationException("Credenciais AWS resolvidas, mas vazias.");
            }

            return new ResolvedAwsCredentials(
                credentials,
                source: "AwsSdkDefaultChain",
                reference: "shared-config/env/role");
        }
        catch (Exception ex)
        {
            failures.Add("AwsSdkDefaultChain: " + ex.GetType().Name);
        }

        throw new InvalidOperationException(
            "Nenhuma credencial AWS valida foi encontrada. Fontes tentadas: " + string.Join(" | ", failures));
    }

    private static AwsAccessKeyPair ReadProtectedAwsKeysOrThrow(string protectedBase64)
    {
        var raw = DpapiSecretProtector.UnprotectBase64OrThrow(protectedBase64);
        var parts = raw.Split(new[] { '\n' }, 2, StringSplitOptions.None);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException("Formato invalido da credencial AWS protegida. Esperado: AccessKeyId\\nSecretAccessKey.");
        }

        var accessKeyId = parts[0].Trim();
        var secretAccessKey = parts[1].Trim();
        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey))
        {
            throw new InvalidOperationException("Credencial AWS protegida invalida (campos vazios).");
        }

        return new AwsAccessKeyPair(accessKeyId, secretAccessKey);
    }
}
