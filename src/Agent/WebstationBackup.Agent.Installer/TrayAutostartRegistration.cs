using System;
using Microsoft.Win32;

namespace WebstationBackup.Agent.Installer;

internal static class TrayAutostartRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WebstationBackupAgentTray";

    public static void SetEnabled(string trayExecutablePath, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(trayExecutablePath))
        {
            throw new ArgumentException("trayExecutablePath é obrigatório.", nameof(trayExecutablePath));
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Nao foi possivel abrir a chave de inicializacao do usuario.");

        if (enabled)
        {
            key.SetValue(ValueName, $"\"{trayExecutablePath.Trim()}\"", RegistryValueKind.String);
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

