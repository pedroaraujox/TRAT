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
    public static ResolvedAwsCredentials ResolveOrThrow(string? targetName)
    {
        var failures = new List<string>(capacity: 2);

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
}
