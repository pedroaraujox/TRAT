[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$PackageOutputDir = "artifacts\trat-local",
    [string]$WindowsRuntimeIdentifier = "win-x64",
    [switch]$SkipZip,
    [switch]$SkipAgentArtifacts,
    [ValidateSet("local", "hml")]
    [string]$EnvironmentProfile = "local"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "src\ControlPlane\ControlPlane.Api\ControlPlane.Api.csproj"
$deployRoot = Join-Path $repoRoot "deploy\controlplane"
$sourceAgentArtifactsDir = Join-Path $repoRoot ("artifacts\agent-package-{0}" -f $EnvironmentProfile)
$outputRoot = Join-Path $repoRoot $PackageOutputDir
$packageDir = Join-Path $outputRoot "TRAT.ControlPlane.Local"
$zipPath = Join-Path $outputRoot "TRAT.ControlPlane.Local.zip"
$stopScript = Join-Path $deployRoot "Parar-Painel-Local.ps1"

# Garante que o painel local nao esteja rodando antes de mexer no pacote, para nao
# copiar/preservar arquivos (banco SQLite, logs) parcialmente travados por processo ativo.
if ((Test-Path $packageDir) -and (Test-Path $stopScript)) {
    try {
        & powershell.exe -ExecutionPolicy Bypass -File $stopScript -PackageRoot $packageDir
    }
    catch {
        Write-Host ("Aviso: falha ao parar o painel local antes do publish. {0}" -f $_.Exception.Message) -ForegroundColor Yellow
    }
}

# Preserva dados locais (banco, chaves de criptografia, config de admin, logs) entre
# republishes. Republish e uma operacao de atualizacao de codigo, nao deve destruir
# estado local (clientes, hosts, jobs ja cadastrados no pacote local de teste).
$preserveStagingDir = Join-Path $env:TEMP ("TRAT-ControlPlane-Preserve-" + [Guid]::NewGuid().ToString("N"))
$preservedSomething = $false
if (Test-Path $packageDir) {
    New-Item -ItemType Directory -Force -Path $preserveStagingDir | Out-Null

    $dataSource = Join-Path $packageDir "data"
    if (Test-Path $dataSource) {
        Copy-Item -Path $dataSource -Destination (Join-Path $preserveStagingDir "data") -Recurse -Force
        $preservedSomething = $true
    }

    $logsSource = Join-Path $packageDir "logs"
    if (Test-Path $logsSource) {
        Copy-Item -Path $logsSource -Destination (Join-Path $preserveStagingDir "logs") -Recurse -Force
        $preservedSomething = $true
    }

    $localSettingsSource = Join-Path $packageDir "appsettings.Local.json"
    if (Test-Path $localSettingsSource) {
        Copy-Item -Path $localSettingsSource -Destination (Join-Path $preserveStagingDir "appsettings.Local.json") -Force
        $preservedSomething = $true
    }
}

try {
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
if ($LASTEXITCODE -ne 0) {
    throw "Publish do ControlPlane falhou com codigo $LASTEXITCODE."
}

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

if ($preservedSomething) {
    $preservedData = Join-Path $preserveStagingDir "data"
    if (Test-Path $preservedData) {
        Copy-Item -Path (Join-Path $preservedData "*") -Destination (Join-Path $packageDir "data") -Recurse -Force
    }

    $preservedLogs = Join-Path $preserveStagingDir "logs"
    if (Test-Path $preservedLogs) {
        Copy-Item -Path (Join-Path $preservedLogs "*") -Destination (Join-Path $packageDir "logs") -Recurse -Force
    }

    $preservedLocalSettings = Join-Path $preserveStagingDir "appsettings.Local.json"
    if (Test-Path $preservedLocalSettings) {
        Copy-Item -Path $preservedLocalSettings -Destination (Join-Path $packageDir "appsettings.Local.json") -Force
    }

    Remove-Item -Path $preserveStagingDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Dados locais preservados (data/, logs/, appsettings.Local.json) do pacote anterior." -ForegroundColor Green
}

Copy-Item -Path (Join-Path $repoRoot "Obisidian\TRAT\deploy\controlplane\README.md") -Destination (Join-Path $packageDir "README.md") -Force
Copy-Item -Path (Join-Path $deployRoot "Backup-Painel-Dados.ps1") -Destination (Join-Path $packageDir "Backup-Painel-Dados.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Restaurar-Painel-Dados.ps1") -Destination (Join-Path $packageDir "Restaurar-Painel-Dados.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Coletar-Logs.ps1") -Destination (Join-Path $packageDir "Coletar-Logs.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Instalar-Painel-Como-Servico.ps1") -Destination (Join-Path $packageDir "Instalar-Painel-Como-Servico.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Remover-Painel-Servico.ps1") -Destination (Join-Path $packageDir "Remover-Painel-Servico.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Iniciar-Painel-Local.cmd") -Destination (Join-Path $packageDir "Iniciar-Painel-Local.cmd") -Force
Copy-Item -Path (Join-Path $deployRoot "Parar-Painel-Local.ps1") -Destination (Join-Path $packageDir "Parar-Painel-Local.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Primeira-Configuracao.ps1") -Destination (Join-Path $packageDir "Primeira-Configuracao.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "appsettings.Local.template.json") -Destination (Join-Path $packageDir "appsettings.Local.template.json") -Force
Copy-Item -Path (Join-Path $deployRoot "Instalar-Homologacao-Central.ps1") -Destination (Join-Path $packageDir "Instalar-Homologacao-Central.ps1") -Force
Copy-Item -Path (Join-Path $deployRoot "Validar-Homologacao-Central.ps1") -Destination (Join-Path $packageDir "Validar-Homologacao-Central.ps1") -Force

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
        Write-Host ("Artifacts do Agent nao foram copiados porque {0} nao existe." -f $sourceAgentArtifactsDir) -ForegroundColor Yellow
    }
}
if (-not $SkipZip) {
    Write-Host ("Arquivo ZIP gerado em: {0}" -f $zipPath)
}
}
finally {
    # Se build/publish/validacao falhar depois que o pacote anterior for removido,
    # devolve o estado persistente ao packageDir. Assim uma falha de ferramenta nao
    # transforma um republish em perda de banco/configuracao.
    if (Test-Path $preserveStagingDir) {
        New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
        foreach ($dirName in @("data", "logs")) {
            $preservedDir = Join-Path $preserveStagingDir $dirName
            if (Test-Path $preservedDir) {
                New-Item -ItemType Directory -Force -Path (Join-Path $packageDir $dirName) | Out-Null
                Copy-Item -Path (Join-Path $preservedDir "*") -Destination (Join-Path $packageDir $dirName) -Recurse -Force
            }
        }

        $preservedSettings = Join-Path $preserveStagingDir "appsettings.Local.json"
        if (Test-Path $preservedSettings) {
            Copy-Item -LiteralPath $preservedSettings -Destination (Join-Path $packageDir "appsettings.Local.json") -Force
        }
        Write-Host "Estado local restaurado apos interrupcao/falha do publish." -ForegroundColor Yellow
    }
}
