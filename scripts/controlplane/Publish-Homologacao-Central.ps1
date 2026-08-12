[CmdletBinding()]
param(
    [string]$PublicUrl = "https://trat-hml.outboxtech.com.br",
    [string]$OutputDir = "artifacts\trat-homologacao"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$agentPublish = Join-Path $repoRoot "scripts\agent\Publish-Agent.ps1"
$controlPlanePublish = Join-Path $repoRoot "scripts\controlplane\Publish-ControlPlane.ps1"
$outputRoot = Join-Path $repoRoot $OutputDir
$sourcePackage = Join-Path $repoRoot "artifacts\trat-local\TRAT.ControlPlane.Local"
$targetPackage = Join-Path $outputRoot "TRAT.ControlPlane.Homologacao"
$zipPath = Join-Path $outputRoot "TRAT.ControlPlane.Homologacao.zip"

& powershell.exe -ExecutionPolicy Bypass -File $agentPublish -EnvironmentProfile hml
if ($LASTEXITCODE -ne 0) { throw "Publish do Agent falhou." }

& powershell.exe -ExecutionPolicy Bypass -File $controlPlanePublish -SkipZip
if ($LASTEXITCODE -ne 0) { throw "Publish do ControlPlane falhou." }

if (Test-Path $outputRoot) { Remove-Item -LiteralPath $outputRoot -Recurse -Force }
New-Item -ItemType Directory -Path $targetPackage -Force | Out-Null
$excludedStateNames = @("data", "logs", "backups", "appsettings.Local.json", "controlplane.pid")
Get-ChildItem -LiteralPath $sourcePackage -Force | Where-Object {
    $_.Name -notin $excludedStateNames
} | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $targetPackage -Recurse -Force
}
foreach ($emptyDir in @("data", "data\keys", "logs", "backups")) {
    New-Item -ItemType Directory -Path (Join-Path $targetPackage $emptyDir) -Force | Out-Null
}
if (Test-Path (Join-Path $targetPackage "appsettings.Local.json")) {
    throw "Pacote de homologacao contem appsettings.Local.json. Publicacao recusada para evitar vazamento de segredo."
}
if (Test-Path (Join-Path $targetPackage "data\controlplane.db")) {
    throw "Pacote de homologacao contem banco local. Publicacao recusada para evitar vazamento de dados."
}

foreach ($fileName in @("Instalar-Homologacao-Central.ps1", "Validar-Homologacao-Central.ps1")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot ("deploy\controlplane\" + $fileName)) -Destination (Join-Path $targetPackage $fileName) -Force
}

Compress-Archive -Path (Join-Path $targetPackage "*") -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Pacote central de homologacao gerado: $targetPackage" -ForegroundColor Green
Write-Host "ZIP para transferencia: $zipPath"
