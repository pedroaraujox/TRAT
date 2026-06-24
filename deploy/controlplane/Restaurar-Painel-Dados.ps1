[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,
    [Parameter(Mandatory = $true)]
    [string]$BackupDbPath
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

$pidFile = Join-Path $packageRootPath "controlplane.pid"
if (Test-Path $pidFile) {
    $raw = (Get-Content -Path $pidFile -Raw -ErrorAction SilentlyContinue).Trim()
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        try {
            $p = Get-Process -Id ([int]$raw) -ErrorAction Stop
            throw "O painel aparenta estar em execucao (PID=$($p.Id)). Execute Parar-Painel-Local.ps1 antes de restaurar o banco."
        }
        catch {
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

Copy-Item -Path $backupDbFullPath -Destination $dbPath -Force

Write-Host "Banco restaurado com sucesso." -ForegroundColor Green
Write-Host ("- Destino: {0}" -f $dbPath)
Write-Host ("- Origem:  {0}" -f $backupDbFullPath)
