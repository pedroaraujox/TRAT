[CmdletBinding()]
param(
    [switch]$RebuildLocalPackage,
    [switch]$ResetLocalState
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$packageRoot = Join-Path $repoRoot "artifacts\trat-local\TRAT.ControlPlane.Local"
$packageStartScript = Join-Path $packageRoot "Iniciar-Painel-Local.cmd"
$publishScript = Join-Path $repoRoot "scripts\controlplane\Publish-ControlPlane.ps1"
$resetScript = Join-Path $repoRoot "Resetar-TRAT.ps1"
$serviceName = "TRATControlPlane"
$panelUrl = "http://localhost:5080"

function Test-IsAdministrator {
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
    }
}

function Try-StartControlPlaneService {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServiceName
    )

    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return $false
    }

    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
        Start-Service -Name $ServiceName -ErrorAction Stop
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))
    }

    return $true
}

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

if ($ResetLocalState) {
    if (-not (Test-Path $resetScript)) {
        throw "Script de reset local nao encontrado em: $resetScript"
    }

    Write-Host "Resetando estado local do TRAT antes da inicializacao..." -ForegroundColor Yellow
    & powershell.exe -ExecutionPolicy Bypass -File $resetScript
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao resetar o estado local do TRAT."
    }
}

if (-not (Test-Path $packageStartScript)) {
    throw "Pacote local do painel nao encontrado em: $packageStartScript"
}

try {
    if (Try-StartControlPlaneService -ServiceName $serviceName) {
        Write-Host ("TRAT iniciado como Windows Service: {0}" -f $serviceName) -ForegroundColor Green
        Start-Process $panelUrl | Out-Null
        return
    }
}
catch {
    Write-Host ("Falha ao iniciar o servico {0}. {1}" -f $serviceName, $_.Exception.Message) -ForegroundColor Yellow
}

$installServiceScript = Join-Path $packageRoot "Instalar-Painel-Como-Servico.ps1"
if (Test-Path $installServiceScript -and (Test-IsAdministrator)) {
    try {
        Write-Host "Instalando TRAT como Windows Service..." -ForegroundColor Cyan
        & powershell.exe -ExecutionPolicy Bypass -File $installServiceScript -PackageRoot $packageRoot -Url $panelUrl
        if ($LASTEXITCODE -ne 0) {
            throw "Falha ao instalar o servico do TRAT."
        }

        Write-Host ("TRAT instalado e iniciado como Windows Service: {0}" -f $serviceName) -ForegroundColor Green
        Start-Process $panelUrl | Out-Null
        return
    }
    catch {
        Write-Host ("Falha ao instalar/iniciar o servico do TRAT. {0}" -f $_.Exception.Message) -ForegroundColor Yellow
    }
}

Write-Host "Iniciando TRAT a partir do pacote local (sem janela do painel)..." -ForegroundColor Green
& $packageStartScript
if ($LASTEXITCODE -ne 0) {
    throw "Falha ao iniciar o painel local do TRAT."
}
