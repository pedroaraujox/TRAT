using System;
using System.Security.Cryptography;
using System.Text;

namespace WebstationBackup.Agent.Installer;

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
}

