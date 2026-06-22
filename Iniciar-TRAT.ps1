[CmdletBinding()]
param(
    [switch]$RebuildLocalPackage
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$packageRoot = Join-Path $repoRoot "artifacts\trat-local\TRAT.ControlPlane.Local"
$packageStartScript = Join-Path $packageRoot "Iniciar-Painel-Local.cmd"
$publishScript = Join-Path $repoRoot "scripts\controlplane\Publish-ControlPlane.ps1"

function Test-DotNetCliAvailable {
    try {
        $null = & dotnet --version
        return $LASTEXITCODE -eq 0
    }
    catch {
        return $false
    }
}

if ($RebuildLocalPackage -or -not (Test-Path $packageStartScript)) {
    if (-not (Test-Path $publishScript)) {
        throw "Script de publish do painel nao encontrado em: $publishScript"
    }

    if (-not (Test-DotNetCliAvailable)) {
        throw "O pacote local do painel nao foi encontrado e o dotnet SDK nao esta disponivel para gera-lo nesta maquina."
    }

    Write-Host "Gerando pacote local do TRAT ControlPlane..." -ForegroundColor Cyan
    & powershell.exe -ExecutionPolicy Bypass -File $publishScript
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao gerar o pacote local do TRAT ControlPlane."
    }
}

if (-not (Test-Path $packageStartScript)) {
    throw "Pacote local do painel nao encontrado em: $packageStartScript"
}

Write-Host "Iniciando TRAT a partir do pacote local..." -ForegroundColor Green
& $packageStartScript
if ($LASTEXITCODE -ne 0) {
    throw "Falha ao iniciar o painel local do TRAT."
}
