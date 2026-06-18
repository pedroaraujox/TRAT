using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows.Forms;

namespace WebstationBackup.Agent.Installer;

internal sealed class InstallerForm : Form
{
    private readonly TextBox _packageRootTextBox;
    private readonly TextBox _settingsOutputTextBox;
    private readonly TextBox _installDirectoryTextBox;
    private readonly TextBox _stateDirectoryTextBox;
    private readonly TextBox _controlPlaneUrlTextBox;
    private readonly TextBox _agentTokenTextBox;
    private readonly CheckBox _protectTokenCheckBox;
    private readonly Button _testControlPlaneButton;
    private readonly Button _testAwsButton;
    private readonly TextBox _customerIdTextBox;
    private readonly TextBox _hostIdTextBox;
    private readonly TextBox _awsRegionTextBox;
    private readonly TextBox _bucketTextBox;
    private readonly TextBox _prefixTextBox;
    private readonly TextBox _credentialTargetTextBox;
    private readonly TextBox _includePathsTextBox;
    private readonly TextBox _excludePathsTextBox;
    private readonly CheckBox _showAdvancedCheckBox;
    private readonly CheckBox _startServiceCheckBox;
    private readonly CheckBox _enableTrayAutostartCheckBox;
    private readonly CheckBox _launchTrayAfterInstallCheckBox;
    private readonly TextBox _outputTextBox;
    private readonly Button _saveSettingsButton;
    private readonly Button _installButton;
    private readonly Button _validateButton;
    private string? _expectedAwsAccountId;

    public InstallerForm()
    {
        Text = "Webstation Backup Agent Installer";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 760);
        Size = new Size(1040, 820);

        var packageRoot = InstallerPackagePaths.ResolveInitialPackageRoot();
        var packageLayout = InstallerPackagePaths.Resolve(packageRoot);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));

        var header = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = "Instalador com GUI do Webstation Backup Agent. O servico roda em segundo plano; esta tela configura e instala o host.",
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold)
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true
        };

        var configurationPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            Padding = new Padding(0, 12, 0, 0)
        };
        configurationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        configurationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        configurationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        _packageRootTextBox = AddTextRow(configurationPanel, 0, "Pasta do pacote", packageRoot, AddBrowseFolderButton(configurationPanel, "Selecionar", () => BrowseFolder(_packageRootTextBox!, UpdateDerivedPaths)));
        _settingsOutputTextBox = AddTextRow(configurationPanel, 1, "Saida settings", InstallerPackagePaths.DefaultSettingsOutputPath(packageRoot), AddBrowseSaveButton(configurationPanel));
        _installDirectoryTextBox = AddTextRow(configurationPanel, 2, "Pasta de instalacao", InstallerPackagePaths.DefaultInstallDirectory, AddBrowseFolderButton(configurationPanel, "Selecionar", () => BrowseFolder(_installDirectoryTextBox!, null)));
        _stateDirectoryTextBox = AddTextRow(configurationPanel, 3, "Pasta de estado", InstallerPackagePaths.DefaultStateDirectory, AddBrowseFolderButton(configurationPanel, "Selecionar", () => BrowseFolder(_stateDirectoryTextBox!, null)));
        _controlPlaneUrlTextBox = AddTextRow(configurationPanel, 4, "ControlPlane URL", "http://localhost:5080", CreateSpacerButton(configurationPanel));
        _agentTokenTextBox = AddTextRow(configurationPanel, 5, "Token do cliente", string.Empty, CreateSpacerButton(configurationPanel), masked: true);
        _customerIdTextBox = AddTextRow(configurationPanel, 6, "CustomerId", string.Empty, CreateSpacerButton(configurationPanel));
        _hostIdTextBox = AddTextRow(configurationPanel, 7, "HostId", Environment.MachineName, CreateSpacerButton(configurationPanel));
        _awsRegionTextBox = AddTextRow(configurationPanel, 8, "AWS region", string.Empty, CreateSpacerButton(configurationPanel));
        _bucketTextBox = AddTextRow(configurationPanel, 9, "Bucket", string.Empty, CreateSpacerButton(configurationPanel));
        _prefixTextBox = AddTextRow(configurationPanel, 10, "Prefixo", string.Empty, CreateSpacerButton(configurationPanel));
        _credentialTargetTextBox = AddTextRow(configurationPanel, 11, "Credential target", string.Empty, CreateSpacerButton(configurationPanel));
        _includePathsTextBox = AddMultilineRow(configurationPanel, 12, "IncludePaths", string.Empty);
        _excludePathsTextBox = AddMultilineRow(configurationPanel, 13, "ExcludePaths", string.Empty);

        _customerIdTextBox.ReadOnly = true;

        _showAdvancedCheckBox = new CheckBox
        {
            Text = "Mostrar opcoes avancadas (AWS, Excludes, etc.)",
            AutoSize = true,
            Checked = false,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 8, 0, 0)
        };
        _showAdvancedCheckBox.CheckedChanged += (_, _) => SetAdvancedVisibility(_showAdvancedCheckBox.Checked);

        _protectTokenCheckBox = new CheckBox
        {
            Text = "Salvar token protegido (DPAPI - LocalMachine) no settings (recomendado)",
            AutoSize = true,
            Checked = true,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 12, 0, 0)
        };

        _startServiceCheckBox = new CheckBox
        {
            Text = "Iniciar servico ao final da instalacao",
            AutoSize = true,
            Checked = false,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 12, 0, 0)
        };

        _enableTrayAutostartCheckBox = new CheckBox
        {
            Text = "Iniciar Tray App com o Windows (usuario atual)",
            AutoSize = true,
            Checked = true,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 6, 0, 0)
        };

        _launchTrayAfterInstallCheckBox = new CheckBox
        {
            Text = "Abrir Tray App ao finalizar a instalacao",
            AutoSize = true,
            Checked = true,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 6, 0, 0)
        };

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 16, 0, 0)
        };

        _validateButton = new Button { Text = "Validar", AutoSize = true };
        _saveSettingsButton = new Button { Text = "Salvar settings", AutoSize = true };
        _installButton = new Button { Text = "Instalar Agent", AutoSize = true };
        _testControlPlaneButton = new Button { Text = "Testar ControlPlane", AutoSize = true };
        _testAwsButton = new Button { Text = "Testar AWS (CLI)", AutoSize = true };
        var openPackageButton = new Button { Text = "Abrir pacote", AutoSize = true };
        var openInstallDirButton = new Button { Text = "Abrir pasta destino", AutoSize = true };

        _validateButton.Click += (_, _) => ValidateCurrentInput(showSuccessMessage: true);
        _saveSettingsButton.Click += (_, _) => SaveSettings(showSuccessMessage: true);
        _installButton.Click += async (_, _) => await InstallAsync();
        _testControlPlaneButton.Click += async (_, _) => await TestControlPlaneAsync();
        _testAwsButton.Click += async (_, _) => await TestAwsAsync();
        openPackageButton.Click += (_, _) => OpenShell(_packageRootTextBox.Text);
        openInstallDirButton.Click += (_, _) => OpenShell(_installDirectoryTextBox.Text);

        actionPanel.Controls.Add(_validateButton);
        actionPanel.Controls.Add(_saveSettingsButton);
        actionPanel.Controls.Add(_installButton);
        actionPanel.Controls.Add(_testControlPlaneButton);
        actionPanel.Controls.Add(_testAwsButton);
        actionPanel.Controls.Add(openPackageButton);
        actionPanel.Controls.Add(openInstallDirButton);

        content.Controls.Add(configurationPanel);
        content.Controls.Add(_showAdvancedCheckBox);
        content.Controls.Add(_protectTokenCheckBox);
        content.Controls.Add(_startServiceCheckBox);
        content.Controls.Add(_enableTrayAutostartCheckBox);
        content.Controls.Add(_launchTrayAfterInstallCheckBox);

        var includeActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 0)
        };
        var addFolderButton = new Button { Text = "Adicionar pasta", AutoSize = true };
        var addFileButton = new Button { Text = "Adicionar arquivo", AutoSize = true };
        addFolderButton.Click += (_, _) => AddIncludeFolder();
        addFileButton.Click += (_, _) => AddIncludeFile();
        includeActions.Controls.Add(addFolderButton);
        includeActions.Controls.Add(addFileButton);
        content.Controls.Add(includeActions);

        content.Controls.Add(actionPanel);

        _outputTextBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f)
        };

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(content, 0, 1);
        root.Controls.Add(_outputTextBox, 0, 2);
        Controls.Add(root);

        AppendOutput("Instalador GUI inicializado.");
        AppendOutput($"Pasta inicial do pacote: {packageLayout.PackageRoot}");
        SetAdvancedVisibility(show: false);
        ValidateCurrentInput(showSuccessMessage: false);
    }

    private void SetAdvancedVisibility(bool show)
    {
        SetRowVisible(_awsRegionTextBox, show);
        SetRowVisible(_bucketTextBox, show);
        SetRowVisible(_prefixTextBox, show);
        SetRowVisible(_credentialTargetTextBox, show);
        SetRowVisible(_excludePathsTextBox, show);
        _startServiceCheckBox.Visible = show;
    }

    private static void SetRowVisible(TextBox textBox, bool visible)
    {
        if (textBox.Tag is Control[] controls)
        {
            foreach (var c in controls)
            {
                c.Visible = visible;
            }
        }

        textBox.Visible = visible;
    }

    private void AddIncludeFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(_includePathsTextBox.Text) ? _includePathsTextBox.Text : Environment.CurrentDirectory,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        AppendLineToMultiline(_includePathsTextBox, dialog.SelectedPath);
    }

    private void AddIncludeFile()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = false,
            Title = "Selecionar arquivo para backup"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        AppendLineToMultiline(_includePathsTextBox, dialog.FileName);
    }

    private static void AppendLineToMultiline(TextBox target, string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        var existing = target.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(existing))
        {
            target.Text = trimmed;
            return;
        }

        if (existing.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Any(x => string.Equals(x.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        target.AppendText(Environment.NewLine + trimmed);
    }

    private async System.Threading.Tasks.Task InstallAsync()
    {
        try
        {
            SetBusy(true);
            AppendOutput("Iniciando instalacao do Agent...");

            var validationErrors = ValidateCurrentInput(showSuccessMessage: false);
            if (validationErrors.Count > 0)
            {
                AppendOutput("Instalacao bloqueada: existem erros de validacao.");
                return;
            }

            var settingsPath = SaveSettings(showSuccessMessage: false);
            await TrySendBootstrapPathsAsync();
            var layout = InstallerPackagePaths.Resolve(_packageRootTextBox.Text);

            var result = await System.Threading.Tasks.Task.Run(() =>
                PowerShellInstallRunner.ExecuteInstall(
                    layout,
                    settingsPath,
                    _installDirectoryTextBox.Text.Trim(),
                    _stateDirectoryTextBox.Text.Trim(),
                    _startServiceCheckBox.Checked));

            AppendOutput($"Install log: {result.LogFilePath}");
            AppendOutput(result.LogContents);

            if (!result.Success)
            {
                MessageBox.Show(
                    $"A instalacao retornou codigo {result.ExitCode}. Revise o log exibido na tela.",
                    "Webstation Backup Agent Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ApplyTrayPreferencesAfterInstall();

            MessageBox.Show(
                "Instalacao concluida com sucesso.",
                "Webstation Backup Agent Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendOutput($"Falha na instalacao: {ex}");
            MessageBox.Show(
                ex.Message,
                "Webstation Backup Agent Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyTrayPreferencesAfterInstall()
    {
        var trayExe = Path.Combine(_installDirectoryTextBox.Text.Trim(), "tray", "WebstationBackup.Agent.Tray.exe");
        if (_enableTrayAutostartCheckBox.Checked)
        {
            TrayAutostartRegistration.SetEnabled(trayExe, enabled: true);
            AppendOutput("Tray App configurado para iniciar com o Windows (usuario atual).");
        }
        else
        {
            TrayAutostartRegistration.SetEnabled(trayExe, enabled: false);
            AppendOutput("Tray App removido da inicializacao automatica do usuario.");
        }

        if (_launchTrayAfterInstallCheckBox.Checked)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = trayExe,
                    UseShellExecute = true
                });
                AppendOutput("Tray App iniciado.");
            }
            catch (Exception ex)
            {
                AppendOutput("Falha ao iniciar Tray App: " + ex.Message);
            }
        }
    }

    private IReadOnlyList<string> ValidateCurrentInput(bool showSuccessMessage)
    {
        var model = BuildModel(applyTokenProtection: true);
        var layout = InstallerPackagePaths.Resolve(_packageRootTextBox.Text);
        var errors = InstallerValidation.Validate(model, layout, _settingsOutputTextBox.Text.Trim());

        if (errors.Count == 0)
        {
            AppendOutput("Validacao concluida sem erros.");
            if (showSuccessMessage)
            {
                MessageBox.Show(
                    "Validacao concluida com sucesso.",
                    "Webstation Backup Agent Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        else
        {
            var builder = new StringBuilder();
            builder.AppendLine("Erros de validacao:");
            foreach (var error in errors)
            {
                builder.AppendLine($"- {error}");
            }

            AppendOutput(builder.ToString());
            if (showSuccessMessage)
            {
                MessageBox.Show(
                    builder.ToString(),
                    "Webstation Backup Agent Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        return errors;
    }

    private string SaveSettings(bool showSuccessMessage)
    {
        var model = BuildModel(applyTokenProtection: true);
        var layout = InstallerPackagePaths.Resolve(_packageRootTextBox.Text);
        var errors = InstallerValidation.Validate(model, layout, _settingsOutputTextBox.Text.Trim());
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Corrija os erros de validacao antes de salvar o settings.");
        }

        var settingsPath = _settingsOutputTextBox.Text.Trim();
        InstallerSettingsWriter.Write(settingsPath, model);
        AppendOutput($"agent.settings.json salvo em: {settingsPath}");

        if (showSuccessMessage)
        {
            MessageBox.Show(
                "agent.settings.json salvo com sucesso.",
                "Webstation Backup Agent Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return settingsPath;
    }

    private async System.Threading.Tasks.Task TrySendBootstrapPathsAsync()
    {
        try
        {
            var baseUrl = _controlPlaneUrlTextBox.Text.Trim().TrimEnd('/');
            var token = _agentTokenTextBox.Text.Trim();
            var customerId = _customerIdTextBox.Text.Trim();
            var hostId = _hostIdTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(baseUrl) ||
                string.IsNullOrWhiteSpace(token) ||
                string.IsNullOrWhiteSpace(customerId) ||
                string.IsNullOrWhiteSpace(hostId))
            {
                return;
            }

            var include = InstallerValidation.ParsePathList(_includePathsTextBox.Text);
            var exclude = InstallerValidation.ParsePathList(_excludePathsTextBox.Text);
            if (include.Length == 0 && exclude.Length == 0)
            {
                return;
            }

            var endpoint = new Uri(baseUrl + "/api/v1/agents/bootstrap/paths");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new
                {
                    hostId,
                    includePaths = include,
                    excludePaths = exclude
                }), Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("X-Agent-Token", token);

            using var resp = await client.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                AppendOutput("Include/Exclude enviados ao ControlPlane (bootstrap).");
                return;
            }

            AppendOutput($"Falha ao enviar Include/Exclude ao ControlPlane (HTTP {(int)resp.StatusCode}).");
        }
        catch (Exception ex)
        {
            AppendOutput("Falha ao enviar Include/Exclude ao ControlPlane: " + ex.Message);
        }
    }

    private InstallerSettingsModel BuildModel(bool applyTokenProtection)
    {
        var model = new InstallerSettingsModel
        {
            CustomerId = _customerIdTextBox.Text,
            HostId = _hostIdTextBox.Text,
            ControlPlaneBaseUrl = _controlPlaneUrlTextBox.Text,
            AgentToken = _agentTokenTextBox.Text,
            AwsRegion = _awsRegionTextBox.Text,
            S3BucketName = _bucketTextBox.Text,
            S3KeyPrefix = _prefixTextBox.Text,
            AwsCredentialTargetName = _credentialTargetTextBox.Text,
            IncludePaths = InstallerValidation.ParsePathList(_includePathsTextBox.Text),
            ExcludePaths = InstallerValidation.ParsePathList(_excludePathsTextBox.Text)
        };

        if (applyTokenProtection && _protectTokenCheckBox.Checked)
        {
            if (!string.IsNullOrWhiteSpace(model.AgentToken) && model.AgentToken.Trim().Length >= 8)
            {
                model.AgentTokenDpapiProtected = DpapiSecretProtector.ProtectToBase64OrThrow(model.AgentToken);
                model.AgentToken = string.Empty;
                model.AgentTokenCredentialTargetName = null;
            }
        }

        return model;
    }

    private void UpdateDerivedPaths()
    {
        if (string.IsNullOrWhiteSpace(_packageRootTextBox.Text))
        {
            return;
        }

        _settingsOutputTextBox.Text = InstallerPackagePaths.DefaultSettingsOutputPath(_packageRootTextBox.Text.Trim());
        AppendOutput($"Pasta do pacote atualizada para: {_packageRootTextBox.Text.Trim()}");
    }

    private void BrowseFolder(TextBox target, Action? afterSelect)
    {
        using var dialog = new FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(target.Text) ? target.Text : Environment.CurrentDirectory,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        target.Text = dialog.SelectedPath;
        afterSelect?.Invoke();
    }

    private Button AddBrowseSaveButton(TableLayoutPanel parent)
    {
        var button = new Button
        {
            Text = "Selecionar",
            AutoSize = true,
            Dock = DockStyle.Fill
        };

        button.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*",
                FileName = Path.GetFileName(_settingsOutputTextBox.Text),
                InitialDirectory = Directory.Exists(Path.GetDirectoryName(_settingsOutputTextBox.Text))
                    ? Path.GetDirectoryName(_settingsOutputTextBox.Text)
                    : _packageRootTextBox.Text
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _settingsOutputTextBox.Text = dialog.FileName;
            }
        };

        return button;
    }

    private Button AddBrowseFolderButton(TableLayoutPanel parent, string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill
        };

        button.Click += (_, _) => action();
        return button;
    }

    private static Control CreateSpacerButton(TableLayoutPanel parent)
    {
        return new Panel { Dock = DockStyle.Fill };
    }

    private TextBox AddTextRow(TableLayoutPanel parent, int rowIndex, string labelText, string initialValue, Control thirdColumn, bool masked = false)
    {
        parent.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 3, 8)
        };

        var textBox = new TextBox
        {
            Text = initialValue,
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = masked
        };
        textBox.Tag = new[] { (Control)label, thirdColumn };

        parent.Controls.Add(label, 0, rowIndex);
        parent.Controls.Add(textBox, 1, rowIndex);
        parent.Controls.Add(thirdColumn, 2, rowIndex);
        return textBox;
    }

    private TextBox AddMultilineRow(TableLayoutPanel parent, int rowIndex, string labelText, string initialValue)
    {
        parent.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 3, 8)
        };

        var textBox = new TextBox
        {
            Text = initialValue,
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };

        var third = new Panel { Dock = DockStyle.Fill };
        textBox.Tag = new[] { (Control)label, third };
        parent.Controls.Add(label, 0, rowIndex);
        parent.Controls.Add(textBox, 1, rowIndex);
        parent.Controls.Add(third, 2, rowIndex);
        return textBox;
    }

    private void AppendOutput(string message)
    {
        _outputTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _validateButton.Enabled = !busy;
        _saveSettingsButton.Enabled = !busy;
        _installButton.Enabled = !busy;
        _testControlPlaneButton.Enabled = !busy;
        _testAwsButton.Enabled = !busy;
    }

    private async System.Threading.Tasks.Task TestControlPlaneAsync()
    {
        try
        {
            SetBusy(true);
            AppendOutput("Testando ControlPlane (ping + enroll)...");
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await ControlPlanePingTester.PingAsync(_controlPlaneUrlTextBox.Text, _agentTokenTextBox.Text, cts.Token);
            AppendOutput($"Ping ControlPlane: {(result.Success ? "OK" : "FALHOU")} - {result.Message}");
            if (!result.Success)
            {
                return;
            }

            var hostId = _hostIdTextBox.Text;
            var hostname = Environment.MachineName;
            var osVersion = Environment.OSVersion.VersionString;

            var enroll = await ControlPlanePingTester.EnrollAsync(_controlPlaneUrlTextBox.Text, _agentTokenTextBox.Text, hostId, hostname, osVersion, cts.Token);
            AppendOutput($"Enroll: {(enroll.Success ? "OK" : "FALHOU")} - {enroll.Message}");
            if (enroll.Success)
            {
                _customerIdTextBox.Text = enroll.CustomerId ?? string.Empty;
                _hostIdTextBox.Text = enroll.HostId ?? hostId;
                _expectedAwsAccountId = enroll.ExpectedAwsAccountId;
                if (!string.IsNullOrWhiteSpace(_expectedAwsAccountId))
                {
                    AppendOutput($"Conta AWS esperada (painel): {_expectedAwsAccountId}");
                }
            }
        }
        catch (Exception ex)
        {
            AppendOutput("Falha ao testar ControlPlane: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async System.Threading.Tasks.Task TestAwsAsync()
    {
        try
        {
            SetBusy(true);
            AppendOutput("Executando precheck AWS via AWS CLI (somente leitura - identidade)...");
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await AwsCliPrecheckRunner.RunIdentityAsync(cts.Token);
            AppendOutput($"AWS CLI: {(result.Success ? "OK" : "FALHOU")} - {result.Message}");
            if (!string.IsNullOrWhiteSpace(result.AwsAccountId))
            {
                AppendOutput($"AWS AccountId: {result.AwsAccountId}");
                if (!string.IsNullOrWhiteSpace(_expectedAwsAccountId) &&
                    !string.Equals(_expectedAwsAccountId.Trim(), result.AwsAccountId.Trim(), StringComparison.Ordinal))
                {
                    AppendOutput($"ALERTA: conta AWS local difere da esperada no painel ({_expectedAwsAccountId}).");
                }
            }
            if (!string.IsNullOrWhiteSpace(result.BucketRegion))
            {
                AppendOutput($"Bucket region: {result.BucketRegion}");
            }
            if ((!result.Success || _showAdvancedCheckBox.Checked) && !string.IsNullOrWhiteSpace(result.RawOutput))
            {
                AppendOutput(result.RawOutput);
            }
        }
        catch (Exception ex)
        {
            AppendOutput("Falha ao testar AWS: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static void OpenShell(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            if (Directory.Exists(target))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                return;
            }

            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
        }
    }
}
