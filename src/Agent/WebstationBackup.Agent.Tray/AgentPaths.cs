using System;
using System.IO;

namespace WebstationBackup.Agent.Tray;

internal static class AgentPaths
{
    public const string ServiceName = "WebstationBackupAgent";
    public const string ServiceDisplayName = "Webstation Backup Agent";

    public static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "WebstationBackup",
        "Agent");

    public static string StateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "WebstationBackup",
        "Agent");

    public static string SettingsFilePath => Path.Combine(StateDirectory, "agent.settings.json");
    public static string LogFilePath => Path.Combine(StateDirectory, "agent.log.jsonl");
    public static string RulesFilePath => Path.Combine(StateDirectory, "project.rules.json");
}
