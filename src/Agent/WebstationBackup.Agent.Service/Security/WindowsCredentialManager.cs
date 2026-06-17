using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WebstationBackup.Agent.Service.Security;

internal sealed record AwsAccessKeyPair(string AccessKeyId, string SecretAccessKey);

internal static class WindowsCredentialManager
{
    public static AwsAccessKeyPair ReadAwsKeysOrThrow(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new ArgumentException("targetName é obrigatório.", nameof(targetName));
        }

        if (!CredRead(targetName, CRED_TYPE_GENERIC, 0, out var pCred))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao ler credencial do Windows Credential Manager.");
        }

        try
        {
            var cred = (CREDENTIAL)Marshal.PtrToStructure(pCred, typeof(CREDENTIAL));
            if (cred.CredentialBlobSize <= 0 || cred.CredentialBlob == IntPtr.Zero)
            {
                throw new InvalidOperationException("Credencial encontrada, mas sem blob.");
            }

            var charCount = checked((int)(cred.CredentialBlobSize / 2u));
            var blob = Marshal.PtrToStringUni(cred.CredentialBlob, charCount) ?? string.Empty;
            var parts = blob.Split(new[] { '\n' }, 2, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException("Formato inválido da credencial. Esperado: AccessKeyId\\nSecretAccessKey.");
            }

            var accessKeyId = parts[0].Trim();
            var secretAccessKey = parts[1].Trim();

            if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey))
            {
                throw new InvalidOperationException("Credencial inválida (campos vazios).");
            }

            return new AwsAccessKeyPair(accessKeyId, secretAccessKey);
        }
        finally
        {
            CredFree(pCred);
        }
    }

    private const int CRED_TYPE_GENERIC = 1;

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree([In] IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
