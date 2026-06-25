using System;
using System.Drawing;
using System.Windows.Forms;

namespace WebstationBackup.Agent.Tray;

internal sealed class TrayStatusForm : Form
{
    private readonly Label _serviceStatusValue;
    private readonly Label _summaryValue;
    private readonly Label _detailsValue;
    private readonly Label _settingsValue;
    private readonly Label _customerValue;
    private readonly Label _hostValue;
    private readonly Label _awsValue;
    private readonly Label _lastLogValue;
    private readonly Label _rulesValue;
    private readonly Button _openPanelButton;
    private readonly Action _openPanel;
    private readonly Action _openConfiguration;
    private readonly Action _refresh;

    public TrayStatusForm(Action openPanel, Action openConfiguration, Action refresh)
    {
        _openPanel = openPanel;
        _openConfiguration = openConfiguration;
        _refresh = refresh;

        Text = "TRAT Agent";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(540, 360);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(16),
            AutoSize = false
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _serviceStatusValue = AddRow(layout, 0, "Servico");
        _summaryValue = AddRow(layout, 1, "Resumo");
        _detailsValue = AddRow(layout, 2, "Detalhes");
        _settingsValue = AddRow(layout, 3, "Configuracao");
        _customerValue = AddRow(layout, 4, "Cliente");
        _hostValue = AddRow(layout, 5, "Host");
        _awsValue = AddRow(layout, 6, "Destino AWS");
        _lastLogValue = AddRow(layout, 7, "Ultimo log");
        _rulesValue = AddRow(layout, 8, "Ultimas regras");

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false
        };

        _openPanelButton = new Button
        {
            Text = "Abrir painel",
            AutoSize = true
        };
        _openPanelButton.Click += (_, _) => _openPanel();

        var configButton = new Button
        {
            Text = "Configuracao",
            AutoSize = true
        };
        configButton.Click += (_, _) => _openConfiguration();

        var refreshButton = new Button
        {
            Text = "Atualizar",
            AutoSize = true
        };
        refreshButton.Click += (_, _) => _refresh();

        buttonPanel.Controls.Add(_openPanelButton);
        buttonPanel.Controls.Add(configButton);
        buttonPanel.Controls.Add(refreshButton);

        layout.Controls.Add(buttonPanel, 0, 9);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);
    }

    public void ApplyStatus(AgentStatusSnapshot snapshot)
    {
        _serviceStatusValue.Text = snapshot.ServiceStatusLabel;
        _summaryValue.Text = snapshot.Summary;
        _detailsValue.Text = snapshot.Details;
        _settingsValue.Text = snapshot.SettingsStatus;
        _customerValue.Text = snapshot.CustomerId;
        _hostValue.Text = snapshot.HostId;
        _awsValue.Text = snapshot.AwsTarget;
        _lastLogValue.Text = snapshot.LastLogUpdate;
        _rulesValue.Text = snapshot.LastRulesUpdate;
        _openPanelButton.Enabled = snapshot.CanOpenPanel;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    private static Label AddRow(TableLayoutPanel layout, int rowIndex, string labelText)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        var baseFont = SystemFonts.MessageBoxFont ?? Control.DefaultFont;

        var title = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(baseFont, FontStyle.Bold)
        };

        var value = new Label
        {
            Text = "-",
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        layout.Controls.Add(title, 0, rowIndex);
        layout.Controls.Add(value, 1, rowIndex);
        return value;
    }
}
