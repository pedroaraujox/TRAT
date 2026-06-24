[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$packageRoot = Join-Path $repoRoot "artifacts\trat-local\TRAT.ControlPlane.Local"
$stopScript = Join-Path $packageRoot "Parar-Painel-Local.ps1"
$serviceName = "TRATControlPlane"

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    try {
        if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
            Stop-Service -Name $serviceName -Force -ErrorAction Stop
            $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
        }
        Write-Host ("Servico {0} parado." -f $serviceName) -ForegroundColor Green
        return
    }
    catch {
        throw ("Falha ao parar o servico {0}. Execute um PowerShell como Administrador. {1}" -f $serviceName, $_.Exception.Message)
    }
}

if (-not (Test-Path $stopScript)) {
    throw "Script de parada do painel nao encontrado em: $stopScript"
}

& powershell.exe -ExecutionPolicy Bypass -File $stopScript -PackageRoot $packageRoot
if ($LASTEXITCODE -ne 0) {
    throw "Falha ao parar o painel local do TRAT."
}
