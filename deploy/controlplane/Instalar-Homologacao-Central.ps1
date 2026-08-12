[CmdletBinding()]
param(
    [string]$PackageRoot = $PSScriptRoot,
    [string]$ServiceName = "TRATControlPlane",
    [string]$PanelUrl = "http://127.0.0.1:5080",
    [string]$PublicHostname = "trat-hml.outboxtech.com.br",
    [string]$TunnelToken,
    [switch]$SkipTunnel
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Execute este script em um PowerShell como Administrador."
    }
}

function Wait-ControlPlaneHealth {
    param([string]$Url)

    $healthUrl = $Url.TrimEnd('/') + "/api/v1/health"
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            $request = [System.Net.HttpWebRequest]::Create($healthUrl)
            $request.Timeout = 3000
            $request.Headers.Add("X-Forwarded-Proto", "https")
            $response = $request.GetResponse()
            try {
                if ([int]$response.StatusCode -eq 200) { return }
            }
            finally {
                $response.Close()
            }
        }
        catch {
        }
        Start-Sleep -Seconds 1
    }

    throw "O ControlPlane nao respondeu em $healthUrl. Execute Coletar-Logs.ps1."
}

Assert-Administrator
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$packageRootPath = (Resolve-Path -LiteralPath $PackageRoot).Path

foreach ($requiredFile in @("ControlPlane.Api.exe", "Primeira-Configuracao.ps1", "Instalar-Painel-Como-Servico.ps1")) {
    if (-not (Test-Path (Join-Path $packageRootPath $requiredFile))) {
        throw "Pacote incompleto. Arquivo ausente: $requiredFile"
    }
}

if (-not (Test-Path (Join-Path $packageRootPath "appsettings.Local.json"))) {
    & powershell.exe -ExecutionPolicy Bypass -File (Join-Path $packageRootPath "Primeira-Configuracao.ps1") `
        -PackageRoot $packageRootPath -PublicHostname $PublicHostname -InternetFacing
    if ($LASTEXITCODE -ne 0) { throw "Falha na primeira configuracao do ControlPlane." }
}

& powershell.exe -ExecutionPolicy Bypass -File (Join-Path $packageRootPath "Instalar-Painel-Como-Servico.ps1") `
    -PackageRoot $packageRootPath -ServiceName $ServiceName -Url $PanelUrl
if ($LASTEXITCODE -ne 0) { throw "Falha ao instalar o servico do ControlPlane." }

Wait-ControlPlaneHealth -Url $PanelUrl

$backupCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}" -PackageRoot "{1}"' -f `
    (Join-Path $packageRootPath "Backup-Painel-Dados.ps1"), $packageRootPath
schtasks.exe /Create /TN "TRAT - Backup ControlPlane Diario" /SC DAILY /ST 03:00 /RU SYSTEM /RL HIGHEST /TR $backupCommand /F | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Falha ao criar a rotina diaria de backup." }

if (-not $SkipTunnel) {
    if ([string]::IsNullOrWhiteSpace($TunnelToken)) {
        $TunnelToken = Read-Host "Cole o token do Cloudflare Tunnel para $PublicHostname"
    }
    if ([string]::IsNullOrWhiteSpace($TunnelToken)) { throw "Token do Cloudflare Tunnel nao informado." }

    $cloudflaredDir = Join-Path $env:ProgramFiles "Cloudflared\bin"
    $cloudflaredExe = Join-Path $cloudflaredDir "cloudflared.exe"
    New-Item -ItemType Directory -Path $cloudflaredDir -Force | Out-Null
    if (-not (Test-Path $cloudflaredExe)) {
        Write-Host "Baixando cloudflared oficial..." -ForegroundColor Cyan
        $downloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"
        (New-Object Net.WebClient).DownloadFile($downloadUrl, $cloudflaredExe)
    }

    $versionOutput = & $cloudflaredExe --version 2>&1
    if ($LASTEXITCODE -ne 0) { throw "cloudflared invalido: $versionOutput" }

    $existingTunnelService = Get-Service -Name "cloudflared" -ErrorAction SilentlyContinue
    if ($null -ne $existingTunnelService) {
        throw "Ja existe um servico cloudflared. Adicione a rota ao tunel existente ou remova-o conscientemente antes de continuar."
    }

    & $cloudflaredExe service install $TunnelToken
    if ($LASTEXITCODE -ne 0) { throw "Falha ao instalar o Cloudflare Tunnel como servico." }
}

Write-Host "Homologacao central instalada." -ForegroundColor Green
Write-Host "ControlPlane local: $PanelUrl"
Write-Host "Hostname esperado: https://$PublicHostname"
Write-Host "Confirme no Cloudflare que a rota publicada aponta para $PanelUrl."
