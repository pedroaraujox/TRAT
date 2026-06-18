[CmdletBinding()]
param(
    [string]$ServiceName = "WebstationBackupAgent",
    [string]$InstallDir = "C:\Program Files\WebstationBackup\Agent",
    [string]$StateDir = "C:\ProgramData\WebstationBackup\Agent",
    [switch]$RemoveState
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Execute este script em um PowerShell com privilegios de administrador."
    }
}

Assert-Administrator

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
    }

    sc.exe delete $ServiceName | Out-Null

    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    } while ($null -ne $service -and (Get-Date) -lt $deadline)

    if ($null -ne $service) {
        throw "O servico '$ServiceName' nao foi removido dentro do tempo esperado."
    }
}

if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force
}

if ($RemoveState.IsPresent -and (Test-Path $StateDir)) {
    Remove-Item -Path $StateDir -Recurse -Force
}

Write-Host "Agent removido com sucesso."
if (-not $RemoveState.IsPresent) {
    Write-Host "A configuracao em estado local foi preservada."
}
