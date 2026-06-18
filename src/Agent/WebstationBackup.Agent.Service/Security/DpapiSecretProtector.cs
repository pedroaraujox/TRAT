using System;
using System.Security.Cryptography;
using System.Text;

namespace WebstationBackup.Agent.Service.Security;

internal static class DpapiSecretProtector
{
    public static string ProtectToBase64OrThrow(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("secret é obrigatório.", nameof(secret));
        }

        var bytes = Encoding.UTF8.GetBytes(secret.Trim());
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string UnprotectBase64OrThrow(string protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
        {
            throw new ArgumentException("protectedBase64 é obrigatório.", nameof(protectedBase64));
        }

        byte[] protectedBytes;
        try
        {
            protectedBytes = Convert.FromBase64String(protectedBase64.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Token protegido inválido (Base64).", ex);
        }

        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.LocalMachine);
        var secret = Encoding.UTF8.GetString(bytes).Trim();
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Token protegido inválido (vazio após descriptografia).");
        }

        return secret;
    }
}
