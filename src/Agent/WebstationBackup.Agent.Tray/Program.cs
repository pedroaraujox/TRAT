using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WebstationBackup.Agent.Tray;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\WebstationBackupAgentTray";
    private const string ShowStatusEventName = @"Local\WebstationBackupAgentTrayShowStatus";

    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var showStatusEvent = EventWaitHandle.OpenExisting(ShowStatusEventName);
                showStatusEvent.Set();
            }
            catch
            {
            }
            return;
        }

        Application.ThreadException += (_, args) => LogUnhandledException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogUnhandledException(args.ExceptionObject as Exception);

        try
        {
            ApplicationConfiguration.Initialize();
            using var showStatusEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowStatusEventName);
            Application.Run(new AgentTrayApplicationContext(showStatusEvent));
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
