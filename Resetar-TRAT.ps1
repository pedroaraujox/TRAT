[CmdletBinding()]
param(
    [switch]$KeepLogs
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-PackageRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    return Join-Path $RepoRoot "artifacts\trat-local\TRAT.ControlPlane.Local"
}

function Test-TRATRunning {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageRoot
    )

    $service = Get-Service -Name "TRATControlPlane" -ErrorAction SilentlyContinue
    if ($null -ne $service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        return $true
    }

    $pidFile = Join-Path $PackageRoot "controlplane.pid"
    if (-not (Test-Path $pidFile)) {
        return $false
    }

    $rawPid = (Get-Content -Path $pidFile -Raw -ErrorAction SilentlyContinue).Trim()
    if ([string]::IsNullOrWhiteSpace($rawPid)) {
        return $false
    }

    try {
        $process = Get-Process -Id ([int]$rawPid) -ErrorAction Stop
        return $null -ne $process
    }
    catch {
        return $false
    }
}

$repoRoot = $PSScriptRoot
$packageRoot = Get-PackageRoot -RepoRoot $repoRoot

if (-not (Test-Path $packageRoot)) {
    throw "Pacote local do TRAT nao encontrado em: $packageRoot"
}

if (Test-TRATRunning -PackageRoot $packageRoot) {
    throw "O TRAT ainda esta em execucao. Execute Parar-TRAT.ps1 antes de resetar o estado local."
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Join-Path $repoRoot ("artifacts\trat-local\state-backups\{0}" -f $timestamp)
New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

$pathsToBackup = @(
    Join-Path $packageRoot "appsettings.Local.json",
    Join-Path $packageRoot "data\controlplane.db"
)

foreach ($path in $pathsToBackup) {
    if (-not (Test-Path $path)) {
        continue
    }

    Copy-Item -Path $path -Destination (Join-Path $backupRoot (Split-Path $path -Leaf)) -Force
}

$pathsToRemove = @(
    Join-Path $packageRoot "appsettings.Local.json",
    Join-Path $packageRoot "data",
    Join-Path $packageRoot "controlplane.pid"
)

if (-not $KeepLogs) {
    $pathsToRemove += Join-Path $packageRoot "logs"
}

foreach ($path in $pathsToRemove) {
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force
    }
}

foreach ($dirName in @("data", "logs", "artifacts")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot $dirName) | Out-Null
}

Write-Host "Estado local do TRAT resetado com sucesso." -ForegroundColor Green
Write-Host ("Backup de seguranca salvo em: {0}" -f $backupRoot)
if ($KeepLogs) {
    Write-Host "Os logs anteriores foram preservados."
}
