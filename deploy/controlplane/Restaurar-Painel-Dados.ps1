[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,
    [Parameter(Mandatory = $true)]
    [string]$BackupDbPath,
    [string]$ServiceName = "TRATControlPlane"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-ExistingPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    $candidate = $PathValue.Trim().Trim('"')
    if ($candidate.Length -gt 3) { $candidate = $candidate.TrimEnd('\') }
    return (Resolve-Path -LiteralPath $candidate).Path
}

$packageRootPath = Resolve-ExistingPath -PathValue $PackageRoot
$backupDbFullPath = Resolve-ExistingPath -PathValue $BackupDbPath
$backupSourceDb = if (Test-Path -LiteralPath $backupDbFullPath -PathType Container) {
    Join-Path $backupDbFullPath "data\controlplane.db"
} else {
    $backupDbFullPath
}
if (-not (Test-Path -LiteralPath $backupSourceDb -PathType Leaf)) {
    throw "Banco nao encontrado no backup: $backupSourceDb"
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
    throw "O servico $ServiceName esta em execucao. Pare-o antes de restaurar."
}

$pidFile = Join-Path $packageRootPath "controlplane.pid"
if (Test-Path $pidFile) {
    $raw = (Get-Content -Path $pidFile -Raw -ErrorAction SilentlyContinue).Trim()
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        $p = Get-Process -Id ([int]$raw -as [int]) -ErrorAction SilentlyContinue
        if ($null -ne $p) {
            throw "O painel aparenta estar em execucao (PID=$($p.Id)). Execute Parar-Painel-Local.ps1 antes de restaurar o banco."
        }
    }
}

$dataDir = Join-Path $packageRootPath "data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$dbPath = Join-Path $dataDir "controlplane.db"

if (Test-Path $dbPath) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupDir = Join-Path $packageRootPath "backups"
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    Copy-Item -Path $dbPath -Destination (Join-Path $backupDir ("controlplane-before-restore-{0}.db" -f $timestamp)) -Force
}

Copy-Item -LiteralPath $backupSourceDb -Destination $dbPath -Force

if (Test-Path -LiteralPath $backupDbFullPath -PathType Container) {
    $backupKeys = Join-Path $backupDbFullPath "data\keys"
    if (Test-Path $backupKeys) {
        $keysDestination = Join-Path $dataDir "keys"
        New-Item -ItemType Directory -Path $keysDestination -Force | Out-Null
        Copy-Item -Path (Join-Path $backupKeys "*") -Destination $keysDestination -Recurse -Force
    }
}

Write-Host "Banco restaurado com sucesso." -ForegroundColor Green
Write-Host ("- Destino: {0}" -f $dbPath)
Write-Host ("- Origem:  {0}" -f $backupSourceDb)
