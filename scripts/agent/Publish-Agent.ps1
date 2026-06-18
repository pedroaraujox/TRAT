[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$PackageOutputDir = "artifacts\agent-package",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "src\Agent\WebstationBackup.Agent.Service\WebstationBackup.Agent.Service.csproj"
$trayProjectPath = Join-Path $repoRoot "src\Agent\WebstationBackup.Agent.Tray\WebstationBackup.Agent.Tray.csproj"
$installerProjectPath = Join-Path $repoRoot "src\Agent\WebstationBackup.Agent.Installer\WebstationBackup.Agent.Installer.csproj"
$rulesPath = Join-Path $repoRoot "project.rules.json"
$settingsTemplatePath = Join-Path $repoRoot "deploy\agent\agent.settings.template.json"
$packageReadmePath = Join-Path $repoRoot "deploy\agent\README.md"
$trayLauncherPath = Join-Path $repoRoot "deploy\agent\Launch-Agent-Tray.cmd"
$installerLauncherPath = Join-Path $repoRoot "deploy\agent\Launch-Agent-Installer.cmd"
$installScriptPath = Join-Path $repoRoot "scripts\agent\Install-Agent.ps1"
$uninstallScriptPath = Join-Path $repoRoot "scripts\agent\Uninstall-Agent.ps1"
$outputRoot = Join-Path $repoRoot $PackageOutputDir
$packageDir = Join-Path $outputRoot "WebstationBackup.Agent.Package"
$stagingDir = $packageDir
$binDir = Join-Path $stagingDir "bin"
$trayDir = Join-Path $stagingDir "tray"
$installerDir = Join-Path $stagingDir "installer"
$zipPath = Join-Path $outputRoot "WebstationBackup.Agent.Package.zip"

if (Test-Path $outputRoot) {
    Remove-Item -Path $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $binDir | Out-Null
New-Item -ItemType Directory -Force -Path $trayDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

dotnet build $projectPath -c $Configuration
dotnet build $trayProjectPath -c $Configuration
dotnet build $installerProjectPath -c $Configuration

$buildOutputDir = Join-Path $repoRoot ("src\Agent\WebstationBackup.Agent.Service\bin\{0}\net48" -f $Configuration)
$trayOutputDir = Join-Path $repoRoot ("src\Agent\WebstationBackup.Agent.Tray\bin\{0}\net8.0-windows" -f $Configuration)
$installerOutputDir = Join-Path $repoRoot ("src\Agent\WebstationBackup.Agent.Installer\bin\{0}\net8.0-windows" -f $Configuration)
if (-not (Test-Path (Join-Path $buildOutputDir "WebstationBackup.Agent.Service.exe"))) {
    throw "Build do Agent nao gerou o executavel esperado em: $buildOutputDir"
}
if (-not (Test-Path (Join-Path $trayOutputDir "WebstationBackup.Agent.Tray.exe"))) {
    throw "Build do Tray App nao gerou o executavel esperado em: $trayOutputDir"
}
if (-not (Test-Path (Join-Path $installerOutputDir "WebstationBackup.Agent.Installer.exe"))) {
    throw "Build do Installer GUI nao gerou o executavel esperado em: $installerOutputDir"
}

Copy-Item -Path (Join-Path $buildOutputDir "*") -Destination $binDir -Recurse -Force
Copy-Item -Path (Join-Path $trayOutputDir "*") -Destination $trayDir -Recurse -Force
Copy-Item -Path (Join-Path $installerOutputDir "*") -Destination $installerDir -Recurse -Force
Copy-Item -Path $rulesPath -Destination (Join-Path $stagingDir "project.rules.json") -Force
Copy-Item -Path $settingsTemplatePath -Destination (Join-Path $stagingDir "agent.settings.template.json") -Force
Copy-Item -Path $packageReadmePath -Destination (Join-Path $stagingDir "README.md") -Force
Copy-Item -Path $trayLauncherPath -Destination (Join-Path $stagingDir "Launch-Agent-Tray.cmd") -Force
Copy-Item -Path $installerLauncherPath -Destination (Join-Path $stagingDir "Launch-Agent-Installer.cmd") -Force
Copy-Item -Path $installScriptPath -Destination (Join-Path $stagingDir "Install-Agent.ps1") -Force
Copy-Item -Path $uninstallScriptPath -Destination (Join-Path $stagingDir "Uninstall-Agent.ps1") -Force

Copy-Item -Path (Join-Path $installerDir "WebstationBackup.Agent.Installer.exe") -Destination (Join-Path $stagingDir "WebstationBackup.Agent.Setup.exe") -Force

if (-not $SkipZip) {
    Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
}

Write-Host ("Pacote do Agent gerado em: {0}" -f $stagingDir)
if (-not $SkipZip) {
    Write-Host ("Arquivo ZIP gerado em: {0}" -f $zipPath)
}
