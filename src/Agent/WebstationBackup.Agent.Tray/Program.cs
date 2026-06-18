using System.Windows.Forms;

namespace WebstationBackup.Agent.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new AgentTrayApplicationContext());
    }
}
