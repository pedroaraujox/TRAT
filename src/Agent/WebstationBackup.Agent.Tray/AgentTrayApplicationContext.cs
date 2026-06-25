using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WebstationBackup.Agent.Tray;

internal sealed class AgentTrayApplicationContext : ApplicationContext
{
    private const string ProductDisplayName = "TRAT Agent";
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

        _statusForm = new TrayStatusForm(OpenPanel, OpenConfiguration, RefreshStatus);

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(new ToolStripMenuItem("Abrir status", null, (_, _) => ShowStatusForm()));
        contextMenu.Items.Add(_serviceStatusItem);
        contextMenu.Items.Add(_summaryItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_openPanelItem);
        contextMenu.Items.Add(_startupItem);
        contextMenu.Items.Add(new ToolStripMenuItem("Abrir configuracao", null, (_, _) => OpenConfiguration()));
        contextMenu.Items.Add(_refreshItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("Sair", null, (_, _) => ExitTray()));

        _openPanelItem.Click += (_, _) => OpenPanel();
        _startupItem.Click += (_, _) => ToggleStartup();
        _refreshItem.Click += (_, _) => RefreshStatus();

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = ProductDisplayName,
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
        _notifyIcon.ShowBalloonTip(2000, ProductDisplayName, "Agent monitorado em segundo plano pela bandeja do Windows.", ToolTipIcon.Info);
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
                ProductDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        OpenShell(snapshot.ControlPlaneBaseUrl);
    }

    private static void OpenConfiguration()
    {
        var installerPath = FindInstallerExecutablePath();
        if (installerPath is null)
        {
            MessageBox.Show(
                "A tela de configuracao do Agent nao foi localizada nesta instalacao.\n\nAtualize o Agent com o pacote completo do painel para disponibilizar o configurador local.",
                ProductDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        OpenShell(installerPath);
    }

    private static string? FindInstallerExecutablePath()
    {
        var candidates = new[]
        {
            AgentPaths.PreferredInstallerExecutablePath,
            AgentPaths.PreferredInstallerSetupPath,
            Path.Combine(AppContext.BaseDirectory, "..", "installer", "WebstationBackup.Agent.Installer.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "TRAT.Agent.Setup.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "WebstationBackup.Agent.Setup.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "installer", "WebstationBackup.Agent.Installer.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "TRAT.Agent.Setup.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "WebstationBackup.Agent.Setup.exe")
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var normalized = Path.GetFullPath(candidate);
                if (File.Exists(normalized))
                {
                    return normalized;
                }
            }
            catch
            {
            }
        }

        return null;
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
                ProductDisplayName,
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
            ProductDisplayName,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static string BuildNotifyText(AgentStatusSnapshot snapshot)
    {
        var text = $"{ProductDisplayName} - {snapshot.ServiceStatusLabel}";
        return text.Length <= 63 ? text : text.Substring(0, 63);
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield;
        }
        catch
        {
            return SystemIcons.Shield;
        }
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
