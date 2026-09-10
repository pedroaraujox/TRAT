using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace WebstationBackup.Agent.Installer;

internal sealed class InstallerForm : Form
{
    private const string InstallerDisplayName = "TRAT Agent Installer";
    private readonly TextBox _packageRootTextBox;
    private readonly TextBox _settingsOutputTextBox;
    private readonly TextBox _installDirectoryTextBox;
    private readonly TextBox _stateDirectoryTextBox;
    private readonly TextBox _controlPlaneUrlTextBox;
    private readonly TextBox _agentTokenTextBox;
    private readonly Button _testControlPlaneButton;
    private readonly Button _testAwsButton;
    private readonly TextBox _customerIdTextBox;
    private readonly TextBox _hostIdTextBox;
    private readonly TextBox _awsAccessKeyIdTextBox;
    private readonly TextBox _awsSecretAccessKeyTextBox;
    private readonly TextBox _includePathsTextBox;
    private readonly TextBox _excludePathsTextBox;
    private readonly CheckBox _showAdvancedCheckBox;
    private readonly Button _openPackageButton;
    private readonly Button _openInstallDirButton;
    private readonly Button _clearIncludePathsButton;
    private readonly TextBox _outputTextBox;
    private readonly Button _saveSettingsButton;
    private readonly Button _installButton;
    private readonly Button _validateButton;
    private string? _expectedAwsAccountId;

    public InstallerForm()
    {
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        Size = new Size(820, 640);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        var packageRoot = InstallerPackagePaths.ResolveInitialPackageRoot();
        var packageLayout = InstallerPackagePaths.Resolve(packageRoot);
        var packageManifest = InstallerPackagePaths.ReadManifest(packageRoot);
        var environmentName = string.IsNullOrWhiteSpace(packageManifest?.Environment)
            ? "NAO IDENTIFICADO"
            : packageManifest!.Environment!.Trim().ToUpperInvariant();
        var controlPlaneUrl = InstallerPackagePaths.ReadDefaultControlPlaneUrl(packageRoot);
        var packageRevision = string.IsNullOrWhiteSpace(packageManifest?.Revision)
            ? "nao identificada"
            : packageManifest!.Revision!.Trim();
        var shortRevision = packageRevision.Length > 12 ? packageRevision.Substring(0, 12) : packageRevision;
        Text = $"{InstallerDisplayName} - {environmentName} - {shortRevision}";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));

        var header = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = $"Ambiente: {environmentName} | Versao: {shortRevision}{Environment.NewLine}Informe somente o token do ControlPlane e as credenciais AWS para instalar.",
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

        _agentTokenTextBox = AddTextRow(configurationPanel, 0, "Token do ControlPlane", string.Empty, CreateSpacerButton(configurationPanel), masked: true);
        _awsAccessKeyIdTextBox = AddTextRow(configurationPanel, 1, "AWS Access Key", string.Empty, CreateSpacerButton(configurationPanel));
        _awsSecretAccessKeyTextBox = AddTextRow(configurationPanel, 2, "AWS Secret Key", string.Empty, CreateSpacerButton(configurationPanel), masked: true);

        // Valores operacionais internos: nunca sao solicitados ao usuario. A URL vem
        // do pacote e cliente/host sao resolvidos pelo enroll no ControlPlane.
        _controlPlaneUrlTextBox = AddTextRow(configurationPanel, 3, "Destino do painel", controlPlaneUrl, CreateSpacerButton(configurationPanel));

        // Campos avancados (ocultos por padrao): destino do pacote, diretorios locais e
        // identificacao. O bucket/regiao S3 sao definidos no painel (politica do host), nao aqui.
        _packageRootTextBox = AddTextRow(configurationPanel, 4, "Pasta do pacote", packageRoot, AddBrowseFolderButton(configurationPanel, "Selecionar", () => BrowseFolder(_packageRootTextBox!, UpdateDerivedPaths)));
        _settingsOutputTextBox = AddTextRow(configurationPanel, 5, "Saida settings", InstallerPackagePaths.DefaultSettingsOutputPath(packageRoot), AddBrowseSaveButton(configurationPanel));
        _installDirectoryTextBox = AddTextRow(configurationPanel, 6, "Pasta de instalacao", InstallerPackagePaths.DefaultInstallDirectory, AddBrowseFolderButton(configurationPanel, "Selecionar", () => BrowseFolder(_installDirectoryTextBox!, null)));
        _stateDirectoryTextBox = AddTextRow(configurationPanel, 7, "Pasta de estado", InstallerPackagePaths.DefaultStateDirectory, AddBrowseFolderButton(configurationPanel, "Selecionar", () => BrowseFolder(_stateDirectoryTextBox!, null)));
        _customerIdTextBox = AddTextRow(configurationPanel, 8, "CustomerId", string.Empty, CreateSpacerButton(configurationPanel));
        _hostIdTextBox = AddTextRow(configurationPanel, 9, "HostId", Environment.MachineName, CreateSpacerButton(configurationPanel));
        _excludePathsTextBox = AddMultilineRow(configurationPanel, 10, "Exclusoes", string.Empty);

        _customerIdTextBox.ReadOnly = true;

        _showAdvancedCheckBox = new CheckBox
        {
            Text = "Opcoes avancadas",
            AutoSize = true,
            Checked = false,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 10, 0, 0)
        };
        _showAdvancedCheckBox.CheckedChanged += (_, _) => SetAdvancedVisibility(_showAdvancedCheckBox.Checked);

        var actionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 16, 0, 0)
        };

        _validateButton = new Button { Text = "Validar", AutoSize = true };
        _saveSettingsButton = new Button { Text = "Salvar settings", AutoSize = true };
        _installButton = new Button { Text = "Instalar", AutoSize = true };
        _testControlPlaneButton = new Button { Text = "Testar acesso", AutoSize = true };
        _testAwsButton = new Button { Text = "Testar AWS (CLI)", AutoSize = true };
        _openPackageButton = new Button { Text = "Abrir pacote", AutoSize = true };
        _openInstallDirButton = new Button { Text = "Abrir pasta destino", AutoSize = true };

        _validateButton.Click += (_, _) => ValidateCurrentInput(showSuccessMessage: true);
        _saveSettingsButton.Click += (_, _) => SaveSettings(showSuccessMessage: true);
        _installButton.Click += async (_, _) => await InstallAsync();
        _testControlPlaneButton.Click += async (_, _) => await TestControlPlaneAsync();
        _testAwsButton.Click += async (_, _) => await TestAwsAsync();
        _openPackageButton.Click += (_, _) => OpenShell(_packageRootTextBox.Text);
        _openInstallDirButton.Click += (_, _) => OpenShell(_installDirectoryTextBox.Text);

        actionPanel.Controls.Add(_installButton);
        _includePathsTextBox = new TextBox { Text = string.Empty, Visible = false };
        _clearIncludePathsButton = new Button { Visible = false };

        content.Controls.Add(configurationPanel);

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
        AppendOutput($"Ambiente do pacote: {environmentName}");
        AppendOutput($"Destino do painel: {_controlPlaneUrlTextBox.Text}");
        _controlPlaneUrlTextBox.TextChanged += (_, _) => ResetEnrollmentContext();
        _agentTokenTextBox.TextChanged += (_, _) => ResetEnrollmentContext();
        _hostIdTextBox.TextChanged += (_, _) => ResetEnrollmentContext();
        SetRowVisible(_controlPlaneUrlTextBox, visible: false);
        SetAdvancedVisibility(show: false);
    }

    private void TryLoadInstalledSettings()
    {
        try
        {
            var settingsPath = Path.Combine(InstallerPackagePaths.DefaultStateDirectory, "agent.settings.json");
            if (!File.Exists(settingsPath))
            {
                return;
            }

            var settings = JsonSerializer.Deserialize<InstallerSettingsModel>(File.ReadAllText(settingsPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (settings is null)
            {
                return;
            }

            var installedControlPlaneUrl = settings.ControlPlaneBaseUrl?.Trim().TrimEnd('/') ?? string.Empty;
            var packageControlPlaneUrl = _controlPlaneUrlTextBox.Text.Trim().TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(installedControlPlaneUrl) &&
                !string.Equals(installedControlPlaneUrl, packageControlPlaneUrl, StringComparison.OrdinalIgnoreCase))
            {
                AppendOutput($"A URL instalada ({installedControlPlaneUrl}) pertence a outro destino e nao sera reutilizada. O pacote atual usara {packageControlPlaneUrl}.");
            }
            _customerIdTextBox.Text = settings.CustomerId?.Trim() ?? string.Empty;
            _hostIdTextBox.Text = string.IsNullOrWhiteSpace(settings.HostId) ? Environment.MachineName : settings.HostId.Trim();
            _includePathsTextBox.Text = string.Join(Environment.NewLine, settings.IncludePaths ?? Array.Empty<string>());
            _excludePathsTextBox.Text = string.Join(Environment.NewLine, settings.ExcludePaths ?? Array.Empty<string>());

            if (!string.IsNullOrWhiteSpace(settings.AgentTokenDpapiProtected))
            {
                _agentTokenTextBox.Text = DpapiSecretProtector.UnprotectBase64OrThrow(settings.AgentTokenDpapiProtected);
            }
            else
            {
                _agentTokenTextBox.Text = settings.AgentToken?.Trim() ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(settings.AwsCredentialDpapiProtected))
            {
                var credentials = DpapiSecretProtector.UnprotectBase64OrThrow(settings.AwsCredentialDpapiProtected).Split(new[] { '\n' }, 2);
                if (credentials.Length == 2)
                {
                    _awsAccessKeyIdTextBox.Text = credentials[0];
                    _awsSecretAccessKeyTextBox.Text = credentials[1];
                }
            }

            AppendOutput("Identidade, credenciais e pastas da configuracao existente foram carregadas. O destino do painel permanece definido pelo ambiente do pacote.");
        }
        catch (Exception ex)
        {
            AppendOutput("Nao foi possivel carregar a configuracao local existente: " + ex.Message);
        }
    }

    private void SetAdvancedVisibility(bool show)
    {
        SetRowVisible(_packageRootTextBox, show);
        SetRowVisible(_settingsOutputTextBox, show);
        SetRowVisible(_installDirectoryTextBox, show);
        SetRowVisible(_stateDirectoryTextBox, show);
        SetRowVisible(_customerIdTextBox, show);
        SetRowVisible(_hostIdTextBox, show);
        SetRowVisible(_excludePathsTextBox, show);
        _validateButton.Visible = show;
        _saveSettingsButton.Visible = show;
        _testControlPlaneButton.Visible = show;
        _testAwsButton.Visible = show;
        _openPackageButton.Visible = show;
        _openInstallDirButton.Visible = show;
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
            AppendOutput("Iniciando instalacao/atualizacao do Agent...");
            await EnsureEnrollmentAsync(logSuccess: true);

            var validationErrors = ValidateCurrentInput(showSuccessMessage: false);
            if (validationErrors.Count > 0)
            {
                AppendOutput("Instalacao bloqueada: existem erros de validacao.");
                return;
            }

            var settingsPath = SaveSettings(showSuccessMessage: false);
            var layout = InstallerPackagePaths.Resolve(_packageRootTextBox.Text);

            var result = await System.Threading.Tasks.Task.Run(() =>
                PowerShellInstallRunner.ExecuteInstall(
                    layout,
                    settingsPath,
                    _installDirectoryTextBox.Text.Trim(),
                    _stateDirectoryTextBox.Text.Trim(),
                    Application.ExecutablePath,
                    startService: true));

            AppendOutput($"Install log: {result.LogFilePath}");
            AppendOutput(result.LogContents);

            if (!result.Success)
            {
                MessageBox.Show(
                    $"A instalacao retornou codigo {result.ExitCode}. Revise o log exibido na tela.",
                    InstallerDisplayName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ApplyTrayPreferencesAfterInstall();

            MessageBox.Show(
                "Instalacao concluida com sucesso. O Agent agora fica instalado no Windows e futuras atualizacoes podem substituir a versao anterior sem fechar o Tray App manualmente.",
                InstallerDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendOutput($"Falha na instalacao: {ex}");
            MessageBox.Show(
                ex.Message,
                InstallerDisplayName,
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
        TrayAutostartRegistration.SetEnabled(trayExe, enabled: true);
        AppendOutput("Tray App configurado para iniciar com o Windows.");

        try
        {
            var trayProcess = Process.Start(new ProcessStartInfo
            {
                FileName = trayExe,
                UseShellExecute = true
            });
            if (trayProcess is null)
            {
                throw new InvalidOperationException("O Windows nao retornou o processo do Tray App.");
            }

            if (trayProcess.WaitForExit(3000))
            {
                var trayErrorLog = Path.Combine(_stateDirectoryTextBox.Text.Trim(), "tray-error.log");
                throw new InvalidOperationException(
                    $"O Tray App encerrou logo apos iniciar (codigo {trayProcess.ExitCode}). " +
                    $"Consulte {trayErrorLog}.");
            }
            AppendOutput("Tray App iniciado.");
        }
        catch (Exception ex)
        {
            AppendOutput("Falha ao iniciar Tray App: " + ex.Message);
        }
    }

    private IReadOnlyList<string> ValidateCurrentInput(bool showSuccessMessage)
    {
        var model = BuildModel(applyTokenProtection: true);
        var layout = InstallerPackagePaths.Resolve(_packageRootTextBox.Text);
        var errors = InstallerValidation.Validate(model, layout, _settingsOutputTextBox.Text.Trim()).ToList();
        var awsSecretInputError = ValidateAwsSecretInput();
        if (awsSecretInputError is not null)
        {
            errors.Add(awsSecretInputError);
        }

        if (errors.Count == 0)
        {
            AppendOutput("Validacao concluida sem erros.");
            if (showSuccessMessage)
            {
                MessageBox.Show(
                    "Validacao concluida com sucesso.",
                    InstallerDisplayName,
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
                    InstallerDisplayName,
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
        var errors = InstallerValidation.Validate(model, layout, _settingsOutputTextBox.Text.Trim()).ToList();
        var awsSecretInputError = ValidateAwsSecretInput();
        if (awsSecretInputError is not null)
        {
            errors.Add(awsSecretInputError);
        }
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
                InstallerDisplayName,
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
            AwsRegion = string.Empty,
            S3BucketName = string.Empty,
            S3KeyPrefix = string.Empty,
            AwsCredentialTargetName = string.Empty,
            IncludePaths = InstallerValidation.ParsePathList(_includePathsTextBox.Text),
            ExcludePaths = InstallerValidation.ParsePathList(_excludePathsTextBox.Text)
        };

        if (applyTokenProtection)
        {
            if (!string.IsNullOrWhiteSpace(model.AgentToken) && model.AgentToken.Trim().Length >= 8)
            {
                model.AgentTokenDpapiProtected = DpapiSecretProtector.ProtectToBase64OrThrow(model.AgentToken);
                model.AgentToken = string.Empty;
                model.AgentTokenCredentialTargetName = null;
            }

            var accessKeyId = _awsAccessKeyIdTextBox.Text.Trim();
            var secretAccessKey = _awsSecretAccessKeyTextBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(accessKeyId) && !string.IsNullOrWhiteSpace(secretAccessKey))
            {
                model.AwsCredentialDpapiProtected = DpapiSecretProtector.ProtectToBase64OrThrow(accessKeyId + "\n" + secretAccessKey);
            }
        }

        return model;
    }

    private string? ValidateAwsSecretInput()
    {
        var accessKeyId = _awsAccessKeyIdTextBox.Text.Trim();
        var secretAccessKey = _awsSecretAccessKeyTextBox.Text.Trim();
        var hasAccessKeyId = !string.IsNullOrWhiteSpace(accessKeyId);
        var hasSecretAccessKey = !string.IsNullOrWhiteSpace(secretAccessKey);
        if (hasAccessKeyId != hasSecretAccessKey)
        {
            return "Informe AWS Access Key e AWS Secret Key.";
        }

        if (!hasAccessKeyId)
        {
            return "AWS Access Key e AWS Secret Key sao obrigatorias para o MVP.";
        }

        return null;
    }

    private void ResetEnrollmentContext()
    {
        _customerIdTextBox.Text = string.Empty;
        _expectedAwsAccountId = null;
    }

    private async System.Threading.Tasks.Task EnsureEnrollmentAsync(bool logSuccess)
    {
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ping = await ControlPlanePingTester.PingAsync(_controlPlaneUrlTextBox.Text, _agentTokenTextBox.Text, cts.Token);
        AppendOutput($"Ping ControlPlane: {(ping.Success ? "OK" : "FALHOU")} - {ping.Message}");
        if (!ping.Success)
        {
            var destination = _controlPlaneUrlTextBox.Text.Trim().TrimEnd('/');
            var detail = ping.StatusCode == 401
                ? $"O token nao pertence ao painel {destination}. Gere o token nesse mesmo ambiente ou corrija o campo Destino do painel."
                : $"Falha ao conectar no painel {destination}: {ping.Message}";
            throw new InvalidOperationException(detail);
        }

        var hostId = string.IsNullOrWhiteSpace(_hostIdTextBox.Text)
            ? Environment.MachineName
            : _hostIdTextBox.Text.Trim();
        var hostname = Environment.MachineName;
        var osVersion = Environment.OSVersion.VersionString;

        var enroll = await ControlPlanePingTester.EnrollAsync(_controlPlaneUrlTextBox.Text, _agentTokenTextBox.Text, hostId, hostname, osVersion, cts.Token);
        AppendOutput($"Enroll: {(enroll.Success ? "OK" : "FALHOU")} - {enroll.Message}");
        if (!enroll.Success)
        {
            throw new InvalidOperationException("Falha ao registrar o host no ControlPlane.");
        }

        _customerIdTextBox.Text = enroll.CustomerId ?? string.Empty;
        _hostIdTextBox.Text = enroll.HostId ?? hostId;
        _expectedAwsAccountId = enroll.ExpectedAwsAccountId;
        if (logSuccess && !string.IsNullOrWhiteSpace(_expectedAwsAccountId))
        {
            AppendOutput($"Conta AWS esperada (painel): {_expectedAwsAccountId}");
        }
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

    private static TextBox AddStandaloneMultiline(TableLayoutPanel content, Label hintLabel, FlowLayoutPanel actions, string initialValue)
    {
        content.Controls.Add(hintLabel);
        content.Controls.Add(actions);

        var textBox = new TextBox
        {
            Text = initialValue,
            Dock = DockStyle.Top,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 90,
            Margin = new Padding(0, 6, 0, 0)
        };

        content.Controls.Add(textBox);
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
            await EnsureEnrollmentAsync(logSuccess: true);
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
            if (!string.IsNullOrWhiteSpace(result.RawOutput))
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
