[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [Parameter(Mandatory = $true)]
    [ValidateSet("local", "hml", "production")]
    [string]$EnvironmentProfile,
    [string]$PackageOutputDir = "",
    [string]$WindowsRuntimeIdentifier = "win-x64",
    [string]$ControlPlaneBaseUrl = "",
    [string]$BuildRevision = "",
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
if ([string]::IsNullOrWhiteSpace($BuildRevision)) {
    $BuildRevision = $env:GITHUB_SHA
}
if ([string]::IsNullOrWhiteSpace($BuildRevision)) {
    $BuildRevision = (git -C $repoRoot rev-parse HEAD).Trim()
}
if ([string]::IsNullOrWhiteSpace($BuildRevision)) {
    throw "Nao foi possivel determinar a revisao do Agent."
}
$normalizedRevision = $BuildRevision.Trim()
$shortRevision = if ($normalizedRevision.Length -gt 12) { $normalizedRevision.Substring(0, 12) } else { $normalizedRevision }
$agentVersion = "1.0.0+$shortRevision"
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
$outputRoot = [IO.Path]::GetFullPath($outputRoot)
$allowedOutputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $outputRoot.StartsWith($allowedOutputRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'A saida do pacote deve ser uma subpasta de artifacts.'
}
if ((Test-Path -LiteralPath $outputRoot) -and ((Get-Item -LiteralPath $outputRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'A saida do pacote nao pode ser um link/reparse point.'
}
$packageDir = Join-Path $outputRoot "TRAT.Agent.Package"
$stagingDir = $packageDir
$binDir = Join-Path $stagingDir "bin"
$trayDir = Join-Path $stagingDir "tray"
$installerDir = Join-Path $stagingDir "installer"
$zipPath = Join-Path $outputRoot "TRAT.Agent.Package.zip"
$serviceOutputDir = Join-Path $outputRoot "_service-build"
$trayOutputDir = Join-Path $outputRoot "_tray-publish"
$installerOutputDir = Join-Path $outputRoot "_installer-publish"

if (Test-Path $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $binDir | Out-Null
New-Item -ItemType Directory -Force -Path $trayDir | Out-Null
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null

# Etapa 1: builda Service e Tray primeiro, pois o instalador precisa embutir esses
# binarios como payload antes do proprio publish do instalador acontecer.
dotnet build $projectPath -c $Configuration -o $serviceOutputDir -p:Version=$agentVersion -p:InformationalVersion=$agentVersion -p:SourceRevisionId=$normalizedRevision -p:IncludeSourceRevisionInInformationalVersion=false
if ($LASTEXITCODE -ne 0) { throw "Build do Agent Service falhou com codigo $LASTEXITCODE." }
dotnet publish $trayProjectPath -c $Configuration -r $WindowsRuntimeIdentifier --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$agentVersion -p:InformationalVersion=$agentVersion -p:SourceRevisionId=$normalizedRevision -p:IncludeSourceRevisionInInformationalVersion=false -o $trayOutputDir
if ($LASTEXITCODE -ne 0) { throw "Publish do Agent Tray falhou com codigo $LASTEXITCODE." }

if (-not (Test-Path (Join-Path $serviceOutputDir "WebstationBackup.Agent.Service.exe"))) {
    throw "Build do Agent nao gerou o executavel esperado em: $serviceOutputDir"
}
if (-not (Test-Path (Join-Path $trayOutputDir "WebstationBackup.Agent.Tray.exe"))) {
    throw "Publish do Tray App nao gerou o executavel esperado em: $trayOutputDir"
}

Copy-Item -Path (Join-Path $serviceOutputDir "*") -Destination $binDir -Recurse -Force
Copy-Item -Path (Join-Path $trayOutputDir "*") -Destination $trayDir -Recurse -Force
Copy-Item -Path $rulesPath -Destination (Join-Path $stagingDir "project.rules.json") -Force
Copy-Item -Path $settingsTemplatePath -Destination (Join-Path $stagingDir "agent.settings.template.json") -Force
Set-Content -Path (Join-Path $stagingDir "controlplane.url") -Value $normalizedControlPlaneUrl -Encoding ASCII
$packageManifest = [ordered]@{
    schemaVersion = 1
    environment = $EnvironmentProfile
    controlPlaneBaseUrl = $normalizedControlPlaneUrl
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    revision = $normalizedRevision
    version = $agentVersion
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
    if ([IO.Path]::GetFullPath($installerPayloadDir) -ne [IO.Path]::GetFullPath((Join-Path $repoRoot 'src\Agent\WebstationBackup.Agent.Installer\Payload')) -or
        ((Get-Item -LiteralPath $installerPayloadDir).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Diretorio de payload inseguro.' }
    Remove-Item -LiteralPath $installerPayloadDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $installerPayloadDir | Out-Null
Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $installerPayloadZip -CompressionLevel Optimal

# Etapa 3: agora sim publica o instalador, que vai embutir o payload zipado acima
# como recurso (EmbeddedResource condicional no csproj) dentro do proprio exe.
dotnet publish $installerProjectPath -c $Configuration -r $WindowsRuntimeIdentifier --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$agentVersion -p:InformationalVersion=$agentVersion -p:SourceRevisionId=$normalizedRevision -p:IncludeSourceRevisionInInformationalVersion=false -o $installerOutputDir
if ($LASTEXITCODE -ne 0) { throw "Publish do Agent Installer falhou com codigo $LASTEXITCODE." }

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
$environmentSetupName = "TRAT.Agent.Setup.{0}.exe" -f $EnvironmentProfile
Copy-Item -Path (Join-Path $stagingDir "TRAT.Agent.Setup.exe") -Destination (Join-Path $standaloneDir $environmentSetupName) -Force

if (-not $SkipZip) {
    Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
}

# The release manifest is outside the embedded payload to avoid a circular digest.
$packageManifest['setupSha256'] = (Get-FileHash -LiteralPath (Join-Path $standaloneDir 'TRAT.Agent.Setup.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
$packageManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $standaloneDir 'agent-package.manifest.json') -Encoding UTF8
$packageManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagingDir 'agent-package.manifest.json') -Encoding UTF8

$requiredOutputs = @(
    (Join-Path $stagingDir "bin\WebstationBackup.Agent.Service.exe"),
    (Join-Path $stagingDir "tray\WebstationBackup.Agent.Tray.exe"),
    (Join-Path $stagingDir "TRAT.Agent.Setup.exe"),
    (Join-Path $standaloneDir "TRAT.Agent.Setup.exe"),
    (Join-Path $standaloneDir "agent-package.manifest.json"),
    (Join-Path $standaloneDir $environmentSetupName)
)
foreach ($requiredOutput in $requiredOutputs) {
    if (-not (Test-Path -LiteralPath $requiredOutput -PathType Leaf)) {
        throw "Pacote incompleto: arquivo obrigatorio ausente em $requiredOutput"
    }
}
$validatedManifest = Get-Content -LiteralPath (Join-Path $standaloneDir "agent-package.manifest.json") -Raw | ConvertFrom-Json
if ($validatedManifest.environment -ne $EnvironmentProfile -or
    $validatedManifest.controlPlaneBaseUrl.TrimEnd('/') -ne $normalizedControlPlaneUrl -or
    $validatedManifest.revision -ne $normalizedRevision) {
    throw "Pacote invalido: manifesto nao corresponde ao perfil $EnvironmentProfile ($normalizedControlPlaneUrl)."
}

Write-Host ("Pacote do Agent gerado em: {0}" -f $stagingDir)
Write-Host ("Ambiente do Agent: {0} ({1})" -f $EnvironmentProfile, $normalizedControlPlaneUrl)
Write-Host ("Versao do Agent: {0} | Revisao: {1}" -f $agentVersion, $normalizedRevision)
Write-Host ("Executavel autonomo (recomendado para download) gerado em: {0}" -f (Join-Path $standaloneDir "TRAT.Agent.Setup.exe"))
Write-Host ("Executavel identificado por ambiente: {0}" -f (Join-Path $standaloneDir $environmentSetupName))
Write-Host "Validacao estrutural e de ambiente do pacote: OK"
if (-not $SkipZip) {
    Write-Host ("Arquivo ZIP (avancado/opcional) gerado em: {0}" -f $zipPath)
}
