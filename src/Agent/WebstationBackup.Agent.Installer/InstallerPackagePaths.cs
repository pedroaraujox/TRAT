using System;
using System.IO;

namespace WebstationBackup.Agent.Installer;

internal sealed record PackageLayout(
    string PackageRoot,
    string InstallScriptPath,
    string ServiceExecutablePath,
    string TrayExecutablePath,
    string SettingsTemplatePath,
    string RulesPath);

internal static class InstallerPackagePaths
{
    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "TRAT",
        "Agent");

    public static string DefaultStateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "TRAT",
        "Agent");

    public static string DefaultSettingsOutputPath(string packageRoot) => Path.Combine(packageRoot, "agent.settings.json");

    public static string ResolveInitialPackageRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidate = FindPackageRoot(baseDirectory);
        if (candidate is not null)
        {
            return candidate;
        }

        return baseDirectory;
    }

    public static PackageLayout Resolve(string packageRoot)
    {
        var normalizedRoot = Path.GetFullPath(packageRoot.Trim());
        return new PackageLayout(
            PackageRoot: normalizedRoot,
            InstallScriptPath: Path.Combine(normalizedRoot, "Install-Agent.ps1"),
            ServiceExecutablePath: Path.Combine(normalizedRoot, "bin", "WebstationBackup.Agent.Service.exe"),
            TrayExecutablePath: Path.Combine(normalizedRoot, "tray", "WebstationBackup.Agent.Tray.exe"),
            SettingsTemplatePath: Path.Combine(normalizedRoot, "agent.settings.template.json"),
            RulesPath: Path.Combine(normalizedRoot, "project.rules.json"));
    }

    private static string? FindPackageRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        for (var i = 0; i < 6 && current is not null; i++)
        {
            var candidate = current.FullName;
            if (LooksLikePackageRoot(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool LooksLikePackageRoot(string candidate)
    {
        var layout = Resolve(candidate);
        return File.Exists(layout.InstallScriptPath)
            && File.Exists(layout.ServiceExecutablePath)
            && File.Exists(layout.SettingsTemplatePath)
            && File.Exists(layout.RulesPath);
    }
}
