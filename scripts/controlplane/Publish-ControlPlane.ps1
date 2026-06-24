[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$PackageOutputDir = "artifacts\trat-local",
    [string]$WindowsRuntimeIdentifier = "win-x64",
    [switch]$SkipZip,
    [switch]$SkipAgentArtifacts
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "src\ControlPlane\ControlPlane.Api\ControlPlane.Api.csproj"
$deployRoot = Join-Path $repoRoot "deploy\controlplane"
$sourceAgentArtifactsDir = Join-Path $repoRoot "artifacts\agent-package"
$outputRoot = Join-Path $repoRoot $PackageOutputDir
$packageDir = Join-Path $outputRoot "TRAT.ControlPlane.Local"
$zipPath = Join-Path $outputRoot "TRAT.ControlPlane.Local.zip"

if (Test-Path $outputRoot) {
    Remove-Item -Path $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    -r $WindowsRuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $packageDir

$requiredFiles = @(
    "ControlPlane.Api.exe",
    "appsettings.json"
)

foreach ($fileName in $requiredFiles) {
    if (-not (Test-Path (Join-Path $packageDir $fileName))) {
        throw "Publish do ControlPlane nao gerou o arquivo esperado: $fileName"
    }
}

foreach ($dirName in @("data", "data\\keys", "logs", "artifacts")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $packageDir $dirName) | Out-Null
}

Copy-Item -Path (Join-Path $deployRoot "README.md") -Destination (Join-Path $packageDir "README.md") -Force
Copy-Item -Path (Join-Path $deployRoot "Backup-Painel-Dados.ps1") -Destination (Join-Path $packageDir "Backup-Painel-Dados.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Restaurar-Painel-Dados.ps1") -Destination (Join-Path $packageDir "Restaurar-Painel-Dados.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Coletar-Logs.ps1") -Destination (Join-Path $packageDir "Coletar-Logs.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Instalar-Painel-Como-Servico.ps1") -Destination (Join-Path $packageDir "Instalar-Painel-Como-Servico.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Remover-Painel-Servico.ps1") -Destination (Join-Path $packageDir "Remover-Painel-Servico.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Iniciar-Painel-Local.cmd") -Destination (Join-Path $packageDir "Iniciar-Painel-Local.cmd") -Force
Copy-Item -Path (Join-Path $deployRoot "Parar-Painel-Local.ps1") -Destination (Join-Path $packageDir "Parar-Painel-Local.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Primeira-Configuracao.ps1") -Destination (Join-Path $packageDir "Primeira-Configuracao.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "appsettings.Local.template.json") -Destination (Join-Path $packageDir "appsettings.Local.template.json") -Force

if (-not $SkipAgentArtifacts -and (Test-Path $sourceAgentArtifactsDir)) {
    $targetAgentArtifactsDir = Join-Path $packageDir "artifacts\agent-package"
    New-Item -ItemType Directory -Force -Path $targetAgentArtifactsDir | Out-Null
    Copy-Item -Path (Join-Path $sourceAgentArtifactsDir "*") -Destination $targetAgentArtifactsDir -Recurse -Force
}

if (-not $SkipZip) {
    Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
}

Write-Host ("Pacote local do painel gerado em: {0}" -f $packageDir)
if (-not $SkipAgentArtifacts) {
    if (Test-Path $sourceAgentArtifactsDir) {
        Write-Host ("Artifacts do Agent copiados de: {0}" -f $sourceAgentArtifactsDir)
    }
    else {
        Write-Host "Artifacts do Agent nao foram copiados porque artifacts\agent-package nao existe." -ForegroundColor Yellow
    }
}
if (-not $SkipZip) {
    Write-Host ("Arquivo ZIP gerado em: {0}" -f $zipPath)
}
