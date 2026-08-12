[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [Parameter(Mandatory = $true)]
    [ValidateSet("local", "hml", "production")]
    [string]$EnvironmentProfile,
    [string]$PackageOutputDir = "",
    [string]$WindowsRuntimeIdentifier = "win-x64",
    [string]$ControlPlaneBaseUrl = "",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

$profileDefaults = @{
    local = @{ Url = "http://localhost:5080"; Output = "artifacts\agent-package-local" }
    hml = @{ Url = "https://trat-hml.outboxtech.com.br"; Output = "artifacts\agent-package-hml" }
    production = @{ Url = "https://trat.outboxtech.com.br"; Output = "artifacts\agent-package-production" }
}
$profile = $profileDefaults[$EnvironmentProfile]
if ([string]::IsNullOrWhiteSpace($ControlPlaneBaseUrl)) { $ControlPlaneBaseUrl = $profile.Url }
if ([string]::IsNullOrWhiteSpace($PackageOutputDir)) { $PackageOutputDir = $profile.Output }
$normalizedControlPlaneUrl = $ControlPlaneBaseUrl.Trim().TrimEnd('/')
if ($EnvironmentProfile -eq "local" -and $normalizedControlPlaneUrl -ne "http://localhost:5080") {
    throw "O perfil local deve apontar exatamente para http://localhost:5080."
}
if ($EnvironmentProfile -eq "hml" -and $normalizedControlPlaneUrl -ne "https://trat-hml.outboxtech.com.br") {
    throw "O perfil hml deve apontar exatamente para https://trat-hml.outboxtech.com.br."
}
if ($EnvironmentProfile -eq "production" -and $normalizedControlPlaneUrl -ne "https://trat.outboxtech.com.br") {
    throw "O perfil production deve apontar exatamente para https://trat.outboxtech.com.br."
}
$projectPath = Join-Path $repoRoot "src\Agent\WebstationBackup.Agent.Service\WebstationBackup.Agent.Service.csproj"
$trayProjectPath = Join-Path $repoRoot "src\Agent\WebstationBackup.Agent.Tray\WebstationBackup.Agent.Tray.csproj"
$installerProjectPath = Join-Path $repoRoot "src\Agent\WebstationBackup.Agent.Installer\WebstationBackup.Agent.Installer.csproj"
$installerPayloadDir = Join-Path $repoRoot "src\Agent\WebstationBackup.Agent.Installer\Payload"
$installerPayloadZip = Join-Path $installerPayloadDir "AgentPackagePayload.zip"
$rulesPath = Join-Path $repoRoot "project.rules.json"
$settingsTemplatePath = Join-Path $repoRoot "deploy\agent\agent.settings.template.json"
$packageReadmePath = Join-Path $repoRoot "Obisidian\TRAT\deploy\agent\README.md"
$trayLauncherPath = Join-Path $repoRoot "deploy\agent\Launch-Agent-Tray.cmd"
$installerLauncherPath = Join-Path $repoRoot "deploy\agent\Launch-Agent-Installer.cmd"
$installScriptPath = Join-Path $repoRoot "scripts\agent\Install-Agent.ps1"
$uninstallScriptPath = Join-Path $repoRoot "scripts\agent\Uninstall-Agent.ps1"
$updateScriptPath = Join-Path $repoRoot "scripts\agent\Update-Agent.ps1"
$outputRoot = Join-Path $repoRoot $PackageOutputDir
$packageDir = Join-Path $outputRoot "TRAT.Agent.Package"
$stagingDir = $packageDir
$binDir = Join-Path $stagingDir "bin"
$trayDir = Join-Path $stagingDir "tray"
$installerDir = Join-Path $stagingDir "installer"
$zipPath = Join-Path $outputRoot "TRAT.Agent.Package.zip"

if (Test-Path $outputRoot) {
    Remove-Item -Path $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $binDir | Out-Null
New-Item -ItemType Directory -Force -Path $trayDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

# Etapa 1: builda Service e Tray primeiro, pois o instalador precisa embutir esses
# binarios como payload antes do proprio publish do instalador acontecer.
dotnet build $projectPath -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build do Agent Service falhou com codigo $LASTEXITCODE." }
dotnet publish $trayProjectPath -c $Configuration -r $WindowsRuntimeIdentifier --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "Publish do Agent Tray falhou com codigo $LASTEXITCODE." }

$buildOutputDir = Join-Path $repoRoot ("src\Agent\WebstationBackup.Agent.Service\bin\{0}\net48" -f $Configuration)
$trayOutputDir = Join-Path $repoRoot ("src\Agent\WebstationBackup.Agent.Tray\bin\{0}\net8.0-windows\{1}\publish" -f $Configuration, $WindowsRuntimeIdentifier)
if (-not (Test-Path (Join-Path $buildOutputDir "WebstationBackup.Agent.Service.exe"))) {
    throw "Build do Agent nao gerou o executavel esperado em: $buildOutputDir"
}
if (-not (Test-Path (Join-Path $trayOutputDir "WebstationBackup.Agent.Tray.exe"))) {
    throw "Publish do Tray App nao gerou o executavel esperado em: $trayOutputDir"
}

Copy-Item -Path (Join-Path $buildOutputDir "*") -Destination $binDir -Recurse -Force
Copy-Item -Path (Join-Path $trayOutputDir "*") -Destination $trayDir -Recurse -Force
Copy-Item -Path $rulesPath -Destination (Join-Path $stagingDir "project.rules.json") -Force
Copy-Item -Path $settingsTemplatePath -Destination (Join-Path $stagingDir "agent.settings.template.json") -Force
Set-Content -Path (Join-Path $stagingDir "controlplane.url") -Value $normalizedControlPlaneUrl -Encoding ASCII
$packageManifest = [ordered]@{
    schemaVersion = 1
    environment = $EnvironmentProfile
    controlPlaneBaseUrl = $normalizedControlPlaneUrl
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
}
$packageManifest | ConvertTo-Json | Set-Content -Path (Join-Path $stagingDir "agent-package.manifest.json") -Encoding UTF8
Copy-Item -Path $packageReadmePath -Destination (Join-Path $stagingDir "README.md") -Force
Copy-Item -Path $trayLauncherPath -Destination (Join-Path $stagingDir "Launch-Agent-Tray.cmd") -Force
Copy-Item -Path $installerLauncherPath -Destination (Join-Path $stagingDir "Launch-Agent-Installer.cmd") -Force
Copy-Item -Path $installScriptPath -Destination (Join-Path $stagingDir "Install-Agent.ps1") -Force
Copy-Item -Path $uninstallScriptPath -Destination (Join-Path $stagingDir "Uninstall-Agent.ps1") -Force
Copy-Item -Path $updateScriptPath -Destination (Join-Path $stagingDir "Update-Agent.ps1") -Force

# Etapa 2: monta o payload (tudo que o instalador precisa para rodar sozinho, sem a
# pasta do pacote ao lado) e zipa para ser embutido como recurso no Setup.exe.
if (Test-Path $installerPayloadDir) {
    Remove-Item -Path $installerPayloadDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $installerPayloadDir | Out-Null
Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $installerPayloadZip -CompressionLevel Optimal

# Etapa 3: agora sim publica o instalador, que vai embutir o payload zipado acima
# como recurso (EmbeddedResource condicional no csproj) dentro do proprio exe.
dotnet publish $installerProjectPath -c $Configuration -r $WindowsRuntimeIdentifier --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw "Publish do Agent Installer falhou com codigo $LASTEXITCODE." }

$installerOutputDir = Join-Path $repoRoot ("src\Agent\WebstationBackup.Agent.Installer\bin\{0}\net8.0-windows\{1}\publish" -f $Configuration, $WindowsRuntimeIdentifier)
if (-not (Test-Path (Join-Path $installerOutputDir "WebstationBackup.Agent.Installer.exe"))) {
    throw "Publish do Installer GUI nao gerou o executavel esperado em: $installerOutputDir"
}

Copy-Item -Path (Join-Path $installerOutputDir "*") -Destination $installerDir -Recurse -Force

Copy-Item -Path (Join-Path $installerDir "WebstationBackup.Agent.Installer.exe") -Destination (Join-Path $stagingDir "TRAT.Agent.Setup.exe") -Force
Copy-Item -Path (Join-Path $installerDir "WebstationBackup.Agent.Installer.exe") -Destination (Join-Path $stagingDir "WebstationBackup.Agent.Setup.exe") -Force

# Etapa 4: pasta "downloads" com apenas o executavel autonomo, que e o unico
# artefato oferecido na pagina de downloads do painel.
$standaloneDir = Join-Path $outputRoot "TRAT.Agent.Standalone"
New-Item -ItemType Directory -Force -Path $standaloneDir | Out-Null
Copy-Item -Path (Join-Path $stagingDir "TRAT.Agent.Setup.exe") -Destination (Join-Path $standaloneDir "TRAT.Agent.Setup.exe") -Force
Copy-Item -Path (Join-Path $stagingDir "agent-package.manifest.json") -Destination (Join-Path $standaloneDir "agent-package.manifest.json") -Force

if (-not $SkipZip) {
    Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
}

Write-Host ("Pacote do Agent gerado em: {0}" -f $stagingDir)
Write-Host ("Ambiente do Agent: {0} ({1})" -f $EnvironmentProfile, $normalizedControlPlaneUrl)
Write-Host ("Executavel autonomo (recomendado para download) gerado em: {0}" -f (Join-Path $standaloneDir "TRAT.Agent.Setup.exe"))
if (-not $SkipZip) {
    Write-Host ("Arquivo ZIP (avancado/opcional) gerado em: {0}" -f $zipPath)
}
