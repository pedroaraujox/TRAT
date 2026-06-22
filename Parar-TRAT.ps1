[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$packageRoot = Join-Path $repoRoot "artifacts\trat-local\TRAT.ControlPlane.Local"
$stopScript = Join-Path $packageRoot "Parar-Painel-Local.ps1"

if (-not (Test-Path $stopScript)) {
    throw "Script de parada do painel nao encontrado em: $stopScript"
}

& powershell.exe -ExecutionPolicy Bypass -File $stopScript -PackageRoot $packageRoot
if ($LASTEXITCODE -ne 0) {
    throw "Falha ao parar o painel local do TRAT."
}
