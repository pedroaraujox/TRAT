[CmdletBinding()]
param(
    [string]$PackageRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$packageRootPath = (Resolve-Path $PackageRoot).Path
$databasePath = Join-Path $packageRootPath "data\controlplane.db"
$backupRoot = Join-Path $packageRootPath "backups"

if (-not (Test-Path $databasePath)) {
    throw "Banco do painel nao encontrado em: $databasePath"
}

New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = Join-Path $backupRoot ("controlplane-{0}.db" -f $timestamp)
Copy-Item -Path $databasePath -Destination $backupPath -Force

Write-Host ("Backup do painel criado em: {0}" -f $backupPath) -ForegroundColor Green
