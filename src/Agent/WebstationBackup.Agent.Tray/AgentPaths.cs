using System;
using System.IO;

namespace WebstationBackup.Agent.Tray;

internal static class AgentPaths
{
    public const string ServiceName = "WebstationBackupAgent";
    public const string ServiceDisplayName = "TRAT Agent";

    private static string LegacyInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "WebstationBackup",
        "Agent");

    private static string LegacyStateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "WebstationBackup",
        "Agent");

    public static string InstallDirectory
    {
        get
        {
            var preferred = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "TRAT",
                "Agent");

            if (!Directory.Exists(preferred) && Directory.Exists(LegacyInstallDirectory))
            {
                return LegacyInstallDirectory;
            }

            return preferred;
        }
    }

    public static string StateDirectory
    {
        get
        {
            var preferred = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "TRAT",
                "Agent");

            if (!Directory.Exists(preferred) && Directory.Exists(LegacyStateDirectory))
            {
                return LegacyStateDirectory;
            }

            return preferred;
        }
    }

    public static string SettingsFilePath => Path.Combine(StateDirectory, "agent.settings.json");
    public static string LogFilePath => Path.Combine(StateDirectory, "agent.log.jsonl");
    public static string RulesFilePath => Path.Combine(StateDirectory, "project.rules.json");
}
