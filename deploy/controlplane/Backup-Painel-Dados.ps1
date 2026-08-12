[CmdletBinding()]
param(
    [string]$PackageRoot = $PSScriptRoot,
    [string]$ServiceName = "TRATControlPlane"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-PackageRootPath {
    param(
        [string]$PathValue
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($PathValue)) { $PSScriptRoot } else { $PathValue.Trim().Trim('"') }
    if ($candidate.Length -gt 3) {
        $candidate = $candidate.TrimEnd('\')
    }

    return (Resolve-Path -LiteralPath $candidate).Path
}

$packageRootPath = Resolve-PackageRootPath -PathValue $PackageRoot
$databasePath = Join-Path $packageRootPath "data\controlplane.db"
$backupRoot = Join-Path $packageRootPath "backups"

if (-not (Test-Path $databasePath)) {
    throw "Banco do painel nao encontrado em: $databasePath"
}

New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = Join-Path $backupRoot ("controlplane-{0}" -f $timestamp)
New-Item -ItemType Directory -Force -Path $backupPath | Out-Null

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$restartService = $null -ne $service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped
try {
    if ($restartService) {
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
    }

    Copy-Item -LiteralPath (Join-Path $packageRootPath "data") -Destination (Join-Path $backupPath "data") -Recurse -Force
    $localSettings = Join-Path $packageRootPath "appsettings.Local.json"
    if (Test-Path $localSettings) {
        Copy-Item -LiteralPath $localSettings -Destination (Join-Path $backupPath "appsettings.Local.json") -Force
    }
}
finally {
    if ($restartService) { Start-Service -Name $ServiceName -ErrorAction Stop }
}

Write-Host ("Backup do painel criado em: {0}" -f $backupPath) -ForegroundColor Green
