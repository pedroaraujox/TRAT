[CmdletBinding()]
param(
    [string]$PanelUrl = "http://localhost:5080",
    [string]$PackageRoot = "",
    [string]$AdminEmail = "",
    [string]$AdminPassword = "",
    [switch]$SkipSetupDownload
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-ExistingPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    $candidate = $PathValue.Trim().Trim('"')
    if ($candidate.Length -gt 3) {
        $candidate = $candidate.TrimEnd('\')
    }

    return (Resolve-Path -LiteralPath $candidate).Path
}

function Get-RequiredSetting {
    param(
        [Parameter(Mandatory = $true)]
        [psobject]$Node,
        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    $property = $Node.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "Configuracao obrigatoria ausente: $PropertyName"
    }

    return [string]$property.Value
}

function Get-AntiForgeryToken {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Html
    )

    $match = [regex]::Match($Html, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    if (-not $match.Success) {
        throw "Token antiforgery nao encontrado na resposta do painel."
    }

    return $match.Groups[1].Value
}

$email = $null
$password = $null

$packageRootProvided = -not [string]::IsNullOrWhiteSpace($PackageRoot)
if (-not $packageRootProvided) {
    $PackageRoot = Join-Path $PSScriptRoot "artifacts\trat-local\TRAT.ControlPlane.Local"
}

$localSettingsPath = $null
if (Test-Path $PackageRoot) {
    $packageRootPath = Resolve-ExistingPath -PathValue $PackageRoot
    $localSettingsPath = Join-Path $packageRootPath "appsettings.Local.json"
    if (Test-Path $localSettingsPath) {
        $settings = Get-Content -Path $localSettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $controlPlaneSettings = $settings.ControlPlane
        if ($null -eq $controlPlaneSettings) {
            throw "Secao ControlPlane nao encontrada em appsettings.Local.json"
        }

        $bootstrapAdmin = $controlPlaneSettings.BootstrapAdmin
        if ($null -ne $bootstrapAdmin) {
            $emailProperty = $bootstrapAdmin.PSObject.Properties["Email"]
            $passwordProperty = $bootstrapAdmin.PSObject.Properties["Password"]
            if ($null -ne $emailProperty) { $email = $emailProperty.Value }
            if ($null -ne $passwordProperty) { $password = $passwordProperty.Value }
        }
    }
}
elseif ($packageRootProvided) {
    throw "PackageRoot nao encontrado: $PackageRoot"
}

if ([string]::IsNullOrWhiteSpace([string]$email)) {
    $email = $AdminEmail
}
if ([string]::IsNullOrWhiteSpace([string]$password)) {
    $password = $AdminPassword
}

if ([string]::IsNullOrWhiteSpace([string]$email) -or [string]::IsNullOrWhiteSpace([string]$password)) {
    if ($null -ne $localSettingsPath -and -not (Test-Path $localSettingsPath)) {
        throw "Credenciais nao encontradas. Informe -AdminEmail e -AdminPassword ou crie o arquivo $localSettingsPath."
    }

    throw "Credenciais nao encontradas. Informe -AdminEmail e -AdminPassword (a senha bootstrap pode ter sido removida do appsettings.Local.json por seguranca)."
}

$baseUrl = $PanelUrl.TrimEnd('/')
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$loginPage = Invoke-WebRequest -Uri ($baseUrl + "/login") -WebSession $session -UseBasicParsing
if ($loginPage.StatusCode -ne 200) {
    throw "Tela de login retornou status inesperado: $($loginPage.StatusCode)"
}

$token = Get-AntiForgeryToken -Html $loginPage.Content
$form = @{
    __RequestVerificationToken = $token
    email = $email
    password = $password
}

$null = Invoke-WebRequest -Uri ($baseUrl + "/login") -Method POST -Body $form -WebSession $session -UseBasicParsing

$adminPage = Invoke-WebRequest -Uri ($baseUrl + "/admin") -WebSession $session -UseBasicParsing
if ($adminPage.StatusCode -ne 200 -or $adminPage.Content -notmatch 'TRAT') {
    throw "Dashboard do painel nao respondeu corretamente apos autenticacao."
}

$downloadsPage = Invoke-WebRequest -Uri ($baseUrl + "/admin/downloads") -WebSession $session -UseBasicParsing
if ($downloadsPage.StatusCode -ne 200 -or $downloadsPage.Content -notmatch 'TRAT Agent') {
    throw "Pagina de downloads nao respondeu corretamente apos autenticacao."
}

$logoResponse = Invoke-WebRequest -Uri ($baseUrl + "/img/trat-logo.png") -WebSession $session -UseBasicParsing
if ($logoResponse.StatusCode -ne 200 -or $logoResponse.RawContentLength -le 0) {
    throw "Logo publicada do TRAT nao foi encontrada corretamente."
}

$validationRoot = Join-Path $PSScriptRoot "artifacts\tmp-download-verify"
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null

$zipPath = Join-Path $validationRoot "TRAT.Agent.Package.zip"
Invoke-WebRequest -Uri ($baseUrl + "/admin/downloads/agent/zip") -WebSession $session -UseBasicParsing -OutFile $zipPath | Out-Null
if (-not (Test-Path $zipPath)) {
    throw "Download do pacote ZIP do agent nao gerou arquivo local."
}

$zipFile = Get-Item $zipPath
if ($zipFile.Length -le 0) {
    throw "Download do pacote ZIP do agent retornou arquivo vazio."
}

$setupBytes = $null
if (-not $SkipSetupDownload) {
    $setupPath = Join-Path $validationRoot "TRAT.Agent.Setup.exe"
    Invoke-WebRequest -Uri ($baseUrl + "/admin/downloads/agent/setup") -WebSession $session -UseBasicParsing -OutFile $setupPath | Out-Null
    if (-not (Test-Path $setupPath)) {
        throw "Download do setup do agent nao gerou arquivo local."
    }

    $setupFile = Get-Item $setupPath
    if ($setupFile.Length -le 0) {
        throw "Download do setup do agent retornou arquivo vazio."
    }

    $setupBytes = $setupFile.Length
}

Write-Host "Validacao do TRAT concluida com sucesso." -ForegroundColor Green
Write-Host ("- Painel: {0}" -f $baseUrl)
Write-Host ("- Login: OK ({0})" -f $email)
Write-Host ("- Dashboard: OK")
Write-Host ("- Downloads: OK")
Write-Host ("- Logo: OK")
Write-Host ("- ZIP do Agent: {0} bytes" -f $zipFile.Length)
if ($null -ne $setupBytes) {
    Write-Host ("- Setup do Agent: {0} bytes" -f $setupBytes)
}
