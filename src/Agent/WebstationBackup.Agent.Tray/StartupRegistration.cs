using System;
using Microsoft.Win32;

namespace WebstationBackup.Agent.Tray;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WebstationBackupAgentTray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var currentValue = key?.GetValue(ValueName) as string;
        return string.Equals(currentValue, BuildCommandLine(), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Nao foi possivel abrir a chave de inicializacao do usuario.");

        if (enabled)
        {
            key.SetValue(ValueName, BuildCommandLine(), RegistryValueKind.String);
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string BuildCommandLine()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Nao foi possivel determinar o caminho do Tray App.");

        return $"\"{executablePath}\"";
    }
}
