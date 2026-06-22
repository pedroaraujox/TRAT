using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WebstationBackup.Agent.Service.Security;

internal sealed record AwsAccessKeyPair(string AccessKeyId, string SecretAccessKey);

internal static class WindowsCredentialManager
{
    public static string ReadGenericSecretOrThrow(string targetName)
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
            var secret = Marshal.PtrToStringUni(cred.CredentialBlob, charCount) ?? string.Empty;
            secret = secret.Trim();
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException("Credencial inválida (vazia).");
            }

            return secret;
        }
        finally
        {
            CredFree(pCred);
        }
    }

    public static void WriteGenericSecretOrThrow(string targetName, string secret)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new ArgumentException("targetName é obrigatório.", nameof(targetName));
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("secret é obrigatório.", nameof(secret));
        }

        var normalizedTargetName = targetName.Trim();
        var normalizedSecret = secret.Trim();
        var secretBytes = Encoding.Unicode.GetBytes(normalizedSecret);

        if (secretBytes.Length > 5120)
        {
            throw new InvalidOperationException("Secret muito grande para o Windows Credential Manager.");
        }

        var blobPtr = Marshal.AllocHGlobal(secretBytes.Length);
        try
        {
            Marshal.Copy(secretBytes, 0, blobPtr, secretBytes.Length);

            var credential = new CREDENTIAL
            {
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                Comment = "TRAT Agent secret",
                TargetAlias = string.Empty,
                Type = CRED_TYPE_GENERIC,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                TargetName = normalizedTargetName,
                UserName = string.Empty,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = blobPtr
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Falha ao gravar credencial no Windows Credential Manager.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public static void DeleteGenericSecret(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return;
        }

        try
        {
            CredDelete(targetName.Trim(), CRED_TYPE_GENERIC, 0);
        }
        catch
        {
        }
    }

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
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

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
