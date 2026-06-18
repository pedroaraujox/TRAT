using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace WebstationBackup.Agent.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.ThreadException += (_, args) => LogUnhandledException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogUnhandledException(args.ExceptionObject as Exception);

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new AgentTrayApplicationContext());
        }
        catch (Exception ex)
        {
            LogUnhandledException(ex);
            throw;
        }
    }

    private static void LogUnhandledException(Exception? ex)
    {
        if (ex is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(AgentPaths.StateDirectory);
            var logPath = Path.Combine(AgentPaths.StateDirectory, "tray-error.log");
            File.AppendAllText(
                logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}
