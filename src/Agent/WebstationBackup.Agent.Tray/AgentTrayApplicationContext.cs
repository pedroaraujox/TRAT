using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WebstationBackup.Agent.Tray;

internal sealed class AgentTrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _serviceStatusItem;
    private readonly ToolStripMenuItem _summaryItem;
    private readonly ToolStripMenuItem _openPanelItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _refreshItem;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly TrayStatusForm _statusForm;

    public AgentTrayApplicationContext()
    {
        _serviceStatusItem = new ToolStripMenuItem("Servico: carregando") { Enabled = false };
        _summaryItem = new ToolStripMenuItem("Status: carregando") { Enabled = false };
        _openPanelItem = new ToolStripMenuItem("Abrir painel");
        _startupItem = new ToolStripMenuItem("Iniciar com o Windows") { CheckOnClick = true };
        _refreshItem = new ToolStripMenuItem("Atualizar status");

        _statusForm = new TrayStatusForm(OpenPanel, OpenConfigurationFolder, OpenLogsFolder, RefreshStatus);

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(new ToolStripMenuItem("Abrir status", null, (_, _) => ShowStatusForm()));
        contextMenu.Items.Add(_serviceStatusItem);
        contextMenu.Items.Add(_summaryItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_openPanelItem);
        contextMenu.Items.Add(_startupItem);
        contextMenu.Items.Add(new ToolStripMenuItem("Abrir configuracao", null, (_, _) => OpenConfigurationFolder()));
        contextMenu.Items.Add(new ToolStripMenuItem("Abrir logs", null, (_, _) => OpenLogsFolder()));
        contextMenu.Items.Add(_refreshItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("Sair", null, (_, _) => ExitTray()));

        _openPanelItem.Click += (_, _) => OpenPanel();
        _startupItem.Click += (_, _) => ToggleStartup();
        _refreshItem.Click += (_, _) => RefreshStatus();

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "Webstation Backup Agent",
            Visible = true,
            ContextMenuStrip = contextMenu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowStatusForm();

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 15000,
            Enabled = true
        };
        _refreshTimer.Tick += (_, _) => RefreshStatus();

        _startupItem.Checked = StartupRegistration.IsEnabled();
        RefreshStatus();
        _notifyIcon.ShowBalloonTip(2000, "Webstation Backup Agent", "Agent monitorado em segundo plano pela bandeja do Windows.", ToolTipIcon.Info);
    }

    private void RefreshStatus()
    {
        try
        {
            var snapshot = AgentStatusSnapshot.Collect();
            _serviceStatusItem.Text = $"Servico: {snapshot.ServiceStatusLabel}";
            _summaryItem.Text = $"Status: {snapshot.Summary}";
            _notifyIcon.Text = BuildNotifyText(snapshot);
            _openPanelItem.Enabled = snapshot.CanOpenPanel;
            _statusForm.ApplyStatus(snapshot);
        }
        catch (Exception ex)
        {
            _serviceStatusItem.Text = "Servico: erro ao consultar";
            _summaryItem.Text = $"Status: {ex.Message}";
            _openPanelItem.Enabled = false;
            ShowError("Falha ao atualizar o status local do Agent.", ex);
        }
    }

    private void ShowStatusForm()
    {
        _statusForm.Show();
        _statusForm.Activate();
    }

    private void OpenPanel()
    {
        var snapshot = AgentStatusSnapshot.Collect();
        if (!snapshot.CanOpenPanel || string.IsNullOrWhiteSpace(snapshot.ControlPlaneBaseUrl))
        {
            MessageBox.Show(
                "O painel ainda nao esta configurado no agent.settings.json.",
                "Webstation Backup Agent",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        OpenShell(snapshot.ControlPlaneBaseUrl);
    }

    private static void OpenConfigurationFolder()
    {
        Directory.CreateDirectory(AgentPaths.StateDirectory);
        OpenShell(AgentPaths.StateDirectory);
    }

    private static void OpenLogsFolder()
    {
        Directory.CreateDirectory(AgentPaths.StateDirectory);
        OpenShell(AgentPaths.StateDirectory);
    }

    private static void OpenShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Falha ao abrir '{target}'.\n\n{ex.Message}",
                "Webstation Backup Agent",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ExitTray()
    {
        _refreshTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _statusForm.Dispose();
        ExitThread();
    }

    private void ToggleStartup()
    {
        try
        {
            StartupRegistration.SetEnabled(_startupItem.Checked);
        }
        catch (Exception ex)
        {
            _startupItem.Checked = StartupRegistration.IsEnabled();
            ShowError("Falha ao atualizar a inicializacao automatica do Tray App.", ex);
        }
    }

    private void ShowError(string title, Exception ex)
    {
        MessageBox.Show(
            $"{title}\n\n{ex.Message}",
            "Webstation Backup Agent",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static string BuildNotifyText(AgentStatusSnapshot snapshot)
    {
        var text = $"Webstation Backup Agent - {snapshot.ServiceStatusLabel}";
        return text.Length <= 63 ? text : text.Substring(0, 63);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _notifyIcon.Dispose();
            _statusForm.Dispose();
        }

        base.Dispose(disposing);
    }
}
