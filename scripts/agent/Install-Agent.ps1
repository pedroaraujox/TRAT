[CmdletBinding()]
param(
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$InstallDir = "C:\Program Files\WebstationBackup\Agent",
    [string]$StateDir = "C:\ProgramData\WebstationBackup\Agent",
    [string]$ServiceName = "WebstationBackupAgent",
    [string]$ServiceDisplayName = "Webstation Backup Agent",
    [string]$SettingsSourcePath,
    [string]$RulesSourcePath,
    [pscredential]$ServiceCredential,
    [switch]$StartService
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

function Assert-DotNet48Installed {
    $release = Get-ItemPropertyValue -Path "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" -Name Release -ErrorAction Stop
    if ($release -lt 528040) {
        throw ".NET Framework 4.8 nao encontrado neste host."
    }
}

function Stop-ServiceIfExists {
    param([string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return
    }

    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $Name -Force -ErrorAction Stop
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
    }
}

function Remove-ServiceIfExists {
    param([string]$Name)

    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return
    }

    Stop-ServiceIfExists -Name $Name
    sc.exe delete $Name | Out-Null

    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    } while ($null -ne $service -and (Get-Date) -lt $deadline)

    if ($null -ne $service) {
        throw "O servico '$Name' nao foi removido dentro do tempo esperado."
    }
}

Assert-Administrator
Assert-DotNet48Installed

$resolvedPackageRoot = (Resolve-Path $PackageRoot).Path
$binSourceDir = Join-Path $resolvedPackageRoot "bin"
$traySourceDir = Join-Path $resolvedPackageRoot "tray"
$rootAgentExeSource = Join-Path $resolvedPackageRoot "WebstationBackup.Agent.Service.exe"
$useEmbeddedBinLayout = Test-Path (Join-Path $binSourceDir "WebstationBackup.Agent.Service.exe")
$agentExeSource = if ($useEmbeddedBinLayout) {
    Join-Path $binSourceDir "WebstationBackup.Agent.Service.exe"
} else {
    $rootAgentExeSource
}

if (-not (Test-Path $agentExeSource)) {
    throw "Pacote invalido. Arquivo nao encontrado: $agentExeSource"
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
New-Item -ItemType Directory -Force -Path $StateDir | Out-Null

Stop-ServiceIfExists -Name $ServiceName

if ($useEmbeddedBinLayout) {
    Copy-Item -Path (Join-Path $binSourceDir "*") -Destination $InstallDir -Recurse -Force
} else {
    $runtimeFiles = Get-ChildItem -Path $resolvedPackageRoot -File | Where-Object {
        $_.Name -like "WebstationBackup.Agent.Service.exe*" -or
        $_.Extension -in @(".dll", ".config", ".pdb")
    }

    if ($runtimeFiles.Count -eq 0) {
        throw "Pacote invalido. Nenhum arquivo de runtime do Agent foi encontrado em: $resolvedPackageRoot"
    }

    foreach ($file in $runtimeFiles) {
        Copy-Item -Path $file.FullName -Destination (Join-Path $InstallDir $file.Name) -Force
    }
}

if (Test-Path (Join-Path $traySourceDir "WebstationBackup.Agent.Tray.exe")) {
    $trayInstallDir = Join-Path $InstallDir "tray"
    New-Item -ItemType Directory -Force -Path $trayInstallDir | Out-Null
    Copy-Item -Path (Join-Path $traySourceDir "*") -Destination $trayInstallDir -Recurse -Force
}

$resolvedRulesSource = if ([string]::IsNullOrWhiteSpace($RulesSourcePath)) {
    Join-Path $resolvedPackageRoot "project.rules.json"
} else {
    (Resolve-Path $RulesSourcePath).Path
}

if (-not (Test-Path $resolvedRulesSource)) {
    throw "Arquivo de regras nao encontrado: $resolvedRulesSource"
}

Copy-Item -Path $resolvedRulesSource -Destination (Join-Path $StateDir "project.rules.json") -Force

if (-not [string]::IsNullOrWhiteSpace($SettingsSourcePath)) {
    $resolvedSettingsSource = (Resolve-Path $SettingsSourcePath).Path
    Copy-Item -Path $resolvedSettingsSource -Destination (Join-Path $StateDir "agent.settings.json") -Force
} elseif (-not (Test-Path (Join-Path $StateDir "agent.settings.json"))) {
    $templateSource = Join-Path $resolvedPackageRoot "agent.settings.template.json"
    if (-not (Test-Path $templateSource)) {
        throw "Template de configuracao nao encontrado: $templateSource"
    }

    Copy-Item -Path $templateSource -Destination (Join-Path $StateDir "agent.settings.json") -Force
}

Remove-ServiceIfExists -Name $ServiceName

$agentExeInstalled = Join-Path $InstallDir "WebstationBackup.Agent.Service.exe"
if ($null -ne $ServiceCredential) {
    New-Service `
        -Name $ServiceName `
        -DisplayName $ServiceDisplayName `
        -BinaryPathName ('"{0}"' -f $agentExeInstalled) `
        -StartupType Automatic `
        -Description "Webstation Backup Agent" `
        -Credential $ServiceCredential
} else {
    New-Service `
        -Name $ServiceName `
        -DisplayName $ServiceDisplayName `
        -BinaryPathName ('"{0}"' -f $agentExeInstalled) `
        -StartupType Automatic `
        -Description "Webstation Backup Agent"
}

if ($StartService.IsPresent) {
    Start-Service -Name $ServiceName
}

Write-Host "Agent instalado com sucesso."
Write-Host ("Binarios: {0}" -f $InstallDir)
Write-Host ("Estado/configuracao: {0}" -f $StateDir)

if (-not $StartService.IsPresent) {
    Write-Host "O servico nao foi iniciado automaticamente."
    Write-Host "Revise agent.settings.json e execute um dry-run antes de iniciar o servico."
}
