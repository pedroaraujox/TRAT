[CmdletBinding()]
param(
    [string]$ServiceName = "WebstationBackupAgent",
    [string]$InstallDir = "C:\Program Files\TRAT\Agent",
    [string]$StateDir = "C:\ProgramData\TRAT\Agent",
    [switch]$RemoveState
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$startupValueNames = @("TRATAgentTray", "WebstationBackupAgentTray")
$uninstallKeyPaths = @(
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TRATAgent",
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WebstationBackupAgent"
)
$startMenuFolder = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)) "TRAT"

function Stop-TrayProcessIfExists {
    param(
        [string]$InstallDir
    )

    $expectedTrayPath = Join-Path (Join-Path $InstallDir "tray") "WebstationBackup.Agent.Tray.exe"
    $trayProcesses = Get-Process -Name "WebstationBackup.Agent.Tray" -ErrorAction SilentlyContinue
    foreach ($process in $trayProcesses) {
        try {
            $processPath = $null
            try {
                $processPath = $process.Path
            } catch {
            }

            if ([string]::IsNullOrWhiteSpace($processPath)) {
                continue
            }

            if ((Test-Path $expectedTrayPath) -and
                [string]::Equals(
                    [System.IO.Path]::GetFullPath($processPath),
                    [System.IO.Path]::GetFullPath($expectedTrayPath),
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $process.Id -Force -ErrorAction Stop
                $process.WaitForExit(30000)
            }
        } catch {
            throw "Falha ao encerrar o Tray App instalado. $($_.Exception.Message)"
        }
    }
}

function Remove-TrayStartupRegistration {
    $runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    if (Test-Path $runKeyPath) {
        foreach ($name in $startupValueNames) {
            Remove-ItemProperty -Path $runKeyPath -Name $name -ErrorAction SilentlyContinue
        }
    }
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Execute este script em um PowerShell com privilegios de administrador."
    }
}

Assert-Administrator

$legacyInstallDir = "C:\Program Files\WebstationBackup\Agent"
$legacyStateDir = "C:\ProgramData\WebstationBackup\Agent"
if (-not $MyInvocation.BoundParameters.ContainsKey("InstallDir") -and (Test-Path $legacyInstallDir) -and -not (Test-Path $InstallDir)) {
    $InstallDir = $legacyInstallDir
}
if (-not $MyInvocation.BoundParameters.ContainsKey("StateDir") -and (Test-Path $legacyStateDir) -and -not (Test-Path $StateDir)) {
    $StateDir = $legacyStateDir
}

Stop-TrayProcessIfExists -InstallDir $InstallDir

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

Remove-TrayStartupRegistration

if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force
}

foreach ($key in $uninstallKeyPaths) {
    if (Test-Path $key) {
        Remove-Item -Path $key -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (Test-Path $startMenuFolder) {
    Remove-Item -Path $startMenuFolder -Recurse -Force
}

if ($RemoveState.IsPresent -and (Test-Path $StateDir)) {
    Remove-Item -Path $StateDir -Recurse -Force
}

Write-Host "Agent removido com sucesso."
if (-not $RemoveState.IsPresent) {
    Write-Host "A configuracao em estado local foi preservada."
}
