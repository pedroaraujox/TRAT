using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WebstationBackup.Agent.Installer;

internal sealed record InstallExecutionResult(
    bool Success,
    int ExitCode,
    string LogFilePath,
    string LogContents);

internal static class PowerShellInstallRunner
{
    public static InstallExecutionResult ExecuteInstall(
        PackageLayout layout,
        string settingsOutputPath,
        string installDirectory,
        string stateDirectory,
        bool startService)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(settingsOutputPath);
        ArgumentNullException.ThrowIfNull(installDirectory);
        ArgumentNullException.ThrowIfNull(stateDirectory);

        var logFilePath = Path.Combine(Path.GetTempPath(), $"webstation-agent-install-{Guid.NewGuid():N}.log");
        var wrapperScriptPath = Path.Combine(Path.GetTempPath(), $"webstation-agent-install-{Guid.NewGuid():N}.ps1");

        try
        {
            File.WriteAllText(wrapperScriptPath, BuildWrapperScript(layout, settingsOutputPath, installDirectory, stateDirectory, logFilePath, startService), Encoding.UTF8);

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{wrapperScriptPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = layout.PackageRoot
            }) ?? throw new InvalidOperationException("Nao foi possivel iniciar o processo elevado do PowerShell.");

            process.WaitForExit();

            var contents = File.Exists(logFilePath)
                ? File.ReadAllText(logFilePath, Encoding.UTF8)
                : "Nenhum log foi gerado pelo instalador elevado.";

            return new InstallExecutionResult(
                Success: process.ExitCode == 0,
                ExitCode: process.ExitCode,
                LogFilePath: logFilePath,
                LogContents: contents);
        }
        finally
        {
            TryDelete(wrapperScriptPath);
        }
    }

    private static string BuildWrapperScript(
        PackageLayout layout,
        string settingsOutputPath,
        string installDirectory,
        string stateDirectory,
        string logFilePath,
        bool startService)
    {
        var builder = new StringBuilder();
        builder.AppendLine("$ErrorActionPreference = 'Stop'");
        builder.AppendLine("$ProgressPreference = 'SilentlyContinue'");
        builder.AppendLine($"$logFile = '{EscapePowerShellLiteral(logFilePath)}'");
        builder.AppendLine($"$scriptPath = '{EscapePowerShellLiteral(layout.InstallScriptPath)}'");
        builder.AppendLine($"$packageRoot = '{EscapePowerShellLiteral(layout.PackageRoot)}'");
        builder.AppendLine($"$settingsPath = '{EscapePowerShellLiteral(settingsOutputPath)}'");
        builder.AppendLine($"$installDir = '{EscapePowerShellLiteral(installDirectory)}'");
        builder.AppendLine($"$stateDir = '{EscapePowerShellLiteral(stateDirectory)}'");
        builder.AppendLine("try {");
        builder.AppendLine("  $installArgs = @{");
        builder.AppendLine("    PackageRoot = $packageRoot");
        builder.AppendLine("    SettingsSourcePath = $settingsPath");
        builder.AppendLine("    InstallDir = $installDir");
        builder.AppendLine("    StateDir = $stateDir");
        builder.AppendLine("  }");
        if (startService)
        {
            builder.AppendLine("  $installArgs['StartService'] = $true");
        }
        builder.AppendLine("  & $scriptPath @installArgs *>&1 | Tee-Object -FilePath $logFile");
        builder.AppendLine("  exit 0");
        builder.AppendLine("} catch {");
        builder.AppendLine("  $_ | Out-String | Tee-Object -FilePath $logFile");
        builder.AppendLine("  exit 1");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
