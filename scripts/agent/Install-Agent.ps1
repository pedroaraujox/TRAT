[CmdletBinding()]
param(
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$InstallDir = "C:\Program Files\TRAT\Agent",
    [string]$StateDir = "C:\ProgramData\TRAT\Agent",
    [string]$ServiceName = "WebstationBackupAgent",
    [string]$ServiceDisplayName = "TRAT Agent",
    [string]$SettingsSourcePath,
    [string]$RulesSourcePath,
    [pscredential]$ServiceCredential,
    [switch]$StartService
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$productName = "TRAT Agent"
$publisherName = "TRAT"
$uninstallKeyPath = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TRATAgent"
$legacyUninstallKeyPath = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WebstationBackupAgent"
$startupValueName = "TRATAgentTray"
$legacyStartupValueName = "WebstationBackupAgentTray"
$startMenuFolder = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)) "TRAT"

function Stop-TrayProcessIfExists {
    param(
        [string]$InstallDir
    )

    $expectedTrayPath = Join-Path (Join-Path $InstallDir "tray") "WebstationBackup.Agent.Tray.exe"
    if (-not (Test-Path $expectedTrayPath)) {
        return
    }

    $normalizedTrayPath = [System.IO.Path]::GetFullPath($expectedTrayPath)
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

            $normalizedProcessPath = [System.IO.Path]::GetFullPath($processPath)
            if (-not [string]::Equals($normalizedProcessPath, $normalizedTrayPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            Stop-Process -Id $process.Id -Force -ErrorAction Stop
            $process.WaitForExit(30000)
        } catch {
            throw "Falha ao encerrar o Tray App instalado em '$expectedTrayPath'. $($_.Exception.Message)"
        }
    }
}

function New-ShortcutFile {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$Arguments,
        [string]$WorkingDirectory,
        [string]$Description,
        [string]$IconLocation
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    if (-not [string]::IsNullOrWhiteSpace($Arguments)) {
        $shortcut.Arguments = $Arguments
    }
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $shortcut.WorkingDirectory = $WorkingDirectory
    }
    if (-not [string]::IsNullOrWhiteSpace($Description)) {
        $shortcut.Description = $Description
    }
    if (-not [string]::IsNullOrWhiteSpace($IconLocation)) {
        $shortcut.IconLocation = $IconLocation
    }
    $shortcut.Save()
}

function Register-StartMenuShortcuts {
    param(
        [string]$InstallDir,
        [string]$StateDir
    )

    $trayExePath = Join-Path (Join-Path $InstallDir "tray") "WebstationBackup.Agent.Tray.exe"
    $uninstallScriptPath = Join-Path (Join-Path $StateDir "tools") "Uninstall-Agent.ps1"

    New-Item -ItemType Directory -Force -Path $startMenuFolder | Out-Null

    New-ShortcutFile `
        -ShortcutPath (Join-Path $startMenuFolder "TRAT Agent.lnk") `
        -TargetPath $trayExePath `
        -Arguments "" `
        -WorkingDirectory (Split-Path -Parent $trayExePath) `
        -Description "Abrir status local do TRAT Agent." `
        -IconLocation $trayExePath

    if (Test-Path $uninstallScriptPath) {
        New-ShortcutFile `
            -ShortcutPath (Join-Path $startMenuFolder "Desinstalar TRAT Agent.lnk") `
            -TargetPath "powershell.exe" `
            -Arguments ('-ExecutionPolicy Bypass -File "{0}"' -f $uninstallScriptPath) `
            -WorkingDirectory (Split-Path -Parent $uninstallScriptPath) `
            -Description "Desinstalar o TRAT Agent mantendo os dados locais por padrao." `
            -IconLocation "powershell.exe"
    }
}

function Register-UninstallEntry {
    param(
        [string]$InstallDir,
        [string]$StateDir,
        [string]$DisplayVersion
    )

    $uninstallScriptPath = Join-Path (Join-Path $StateDir "tools") "Uninstall-Agent.ps1"
    New-Item -Path $uninstallKeyPath -Force | Out-Null
    Set-ItemProperty -Path $uninstallKeyPath -Name "DisplayName" -Value $productName
    Set-ItemProperty -Path $uninstallKeyPath -Name "DisplayVersion" -Value $DisplayVersion
    Set-ItemProperty -Path $uninstallKeyPath -Name "Publisher" -Value $publisherName
    Set-ItemProperty -Path $uninstallKeyPath -Name "InstallLocation" -Value $InstallDir
    Set-ItemProperty -Path $uninstallKeyPath -Name "DisplayIcon" -Value (Join-Path (Join-Path $InstallDir "tray") "WebstationBackup.Agent.Tray.exe")
    Set-ItemProperty -Path $uninstallKeyPath -Name "UninstallString" -Value ('powershell.exe -ExecutionPolicy Bypass -File "{0}"' -f $uninstallScriptPath)
    Set-ItemProperty -Path $uninstallKeyPath -Name "QuietUninstallString" -Value ('powershell.exe -ExecutionPolicy Bypass -File "{0}"' -f $uninstallScriptPath)
    Set-ItemProperty -Path $uninstallKeyPath -Name "NoModify" -Value 1 -Type DWord
    Set-ItemProperty -Path $uninstallKeyPath -Name "NoRepair" -Value 1 -Type DWord

    if (Test-Path $legacyUninstallKeyPath) {
        Remove-Item -Path $legacyUninstallKeyPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Write-InstallationMetadata {
    param(
        [string]$InstallDir,
        [string]$StateDir,
        [string]$DisplayVersion
    )

    $metadata = [ordered]@{
        productName = $productName
        publisher = $publisherName
        version = $DisplayVersion
        installedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        installDir = $InstallDir
        stateDir = $StateDir
        serviceName = $ServiceName
        serviceDisplayName = $ServiceDisplayName
        serviceExecutablePath = (Join-Path $InstallDir "WebstationBackup.Agent.Service.exe")
        trayExecutablePath = (Join-Path (Join-Path $InstallDir "tray") "WebstationBackup.Agent.Tray.exe")
        uninstallScriptPath = (Join-Path (Join-Path $StateDir "tools") "Uninstall-Agent.ps1")
        updateScriptPath = (Join-Path (Join-Path $StateDir "tools") "Update-Agent.ps1")
    }

    $metadata | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $StateDir "agent.installation.json") -Encoding UTF8
}

function Copy-MaintenanceScripts {
    param(
        [string]$PackageRoot,
        [string]$StateDir
    )

    $toolsDir = Join-Path $StateDir "tools"
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null

    foreach ($scriptName in @("Install-Agent.ps1", "Uninstall-Agent.ps1", "Update-Agent.ps1")) {
        $sourcePath = Join-Path $PackageRoot $scriptName
        if (Test-Path $sourcePath) {
            Copy-Item -Path $sourcePath -Destination (Join-Path $toolsDir $scriptName) -Force
        }
    }
}

function Get-AgentDisplayVersion {
    param(
        [string]$ExecutablePath
    )

    if (-not (Test-Path $ExecutablePath)) {
        return "1.0.0"
    }

    $versionInfo = (Get-Item $ExecutablePath).VersionInfo
    if (-not [string]::IsNullOrWhiteSpace($versionInfo.ProductVersion)) {
        return $versionInfo.ProductVersion
    }

    if (-not [string]::IsNullOrWhiteSpace($versionInfo.FileVersion)) {
        return $versionInfo.FileVersion
    }

    return "1.0.0"
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Execute este script em um PowerShell com privilegios de administrador."
    }
}

function Get-RegistryValueCompat {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $item = Get-ItemProperty -Path $Path -ErrorAction Stop
    $property = $item.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Valor '$Name' nao encontrado no registro em '$Path'."
    }

    return $property.Value
}

function Assert-DotNet48Installed {
    $release = Get-RegistryValueCompat -Path "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" -Name Release
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

function Ensure-ServiceInstalled {
    param(
        [string]$Name,
        [string]$DisplayName,
        [string]$BinaryPath,
        [string]$ServiceStateDir,
        [pscredential]$Credential
    )

    if ($BinaryPath.Contains('"') -or $ServiceStateDir.Contains('"')) { throw 'Caminho de servico invalido.' }
    $commandLine = '"{0}" --state-dir "{1}"' -f $BinaryPath, $ServiceStateDir
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        if ($null -ne $Credential) {
            New-Service `
                -Name $Name `
                -DisplayName $DisplayName `
                -BinaryPathName $commandLine `
                -StartupType Automatic `
                -Description "TRAT Agent" `
                -Credential $Credential
        } else {
            New-Service `
                -Name $Name `
                -DisplayName $DisplayName `
                -BinaryPathName $commandLine `
                -StartupType Automatic `
                -Description "TRAT Agent"
        }

        return
    }

    Set-Service -Name $Name -DisplayName $DisplayName -StartupType Automatic
    sc.exe config $Name binPath= $commandLine | Out-Null
    if ($LASTEXITCODE) { throw 'Falha ao atualizar caminho do servico.' }
    sc.exe description $Name "TRAT Agent" | Out-Null

    if ($null -ne $Credential) {
        $username = $Credential.UserName
        $password = $Credential.GetNetworkCredential().Password
        sc.exe config $Name obj= $username password= $password | Out-Null
    }
}

function Ensure-ServiceRecovery {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    sc.exe failure $Name reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
}

Assert-Administrator
Assert-DotNet48Installed

$legacyInstallDir = "C:\Program Files\WebstationBackup\Agent"
$legacyStateDir = "C:\ProgramData\WebstationBackup\Agent"
if (-not $MyInvocation.BoundParameters.ContainsKey("InstallDir") -and (Test-Path $legacyInstallDir) -and -not (Test-Path $InstallDir)) {
    $InstallDir = $legacyInstallDir
}
if (-not $MyInvocation.BoundParameters.ContainsKey("StateDir") -and (Test-Path $legacyStateDir) -and -not (Test-Path $StateDir)) {
    $StateDir = $legacyStateDir
}

$resolvedPackageRoot = (Resolve-Path $PackageRoot).Path
$binSourceDir = Join-Path $resolvedPackageRoot "bin"
$traySourceDir = Join-Path $resolvedPackageRoot "tray"
$installerSourceDir = Join-Path $resolvedPackageRoot "installer"
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

# DPAPI LocalMachine relies on filesystem permissions for confidentiality.
# Protect the secret file before copying any credential-bearing contents.
$settingsDestination = Join-Path $StateDir 'agent.settings.json'
if (-not (Test-Path -LiteralPath $settingsDestination)) { New-Item -ItemType File -Path $settingsDestination | Out-Null }
$secretAcl = New-Object System.Security.AccessControl.FileSecurity
$secretAcl.SetAccessRuleProtection($true, $false)
foreach ($sidText in @('S-1-5-18', 'S-1-5-32-544')) {
    $sid = New-Object System.Security.Principal.SecurityIdentifier($sidText)
    $secretAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'Allow')))
}
if ($null -ne $ServiceCredential) {
    $serviceIdentity = New-Object System.Security.Principal.NTAccount($ServiceCredential.UserName)
    $secretAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($serviceIdentity, 'Read', 'Allow')))
} else {
    $existingService = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
    $existingIdentity = if ($null -ne $existingService) { $existingService.StartName } else { $null }
    if (-not [string]::IsNullOrWhiteSpace($existingIdentity) -and $existingIdentity -ne 'LocalSystem') {
        $serviceIdentity = New-Object System.Security.Principal.NTAccount($existingIdentity)
        $secretAcl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($serviceIdentity, 'Read', 'Allow')))
    }
}
Set-Acl -LiteralPath $settingsDestination -AclObject $secretAcl

Stop-ServiceIfExists -Name $ServiceName
Stop-TrayProcessIfExists -InstallDir $InstallDir

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

if (Test-Path (Join-Path $installerSourceDir "WebstationBackup.Agent.Installer.exe")) {
    $installerInstallDir = Join-Path $InstallDir "installer"
    New-Item -ItemType Directory -Force -Path $installerInstallDir | Out-Null
    Copy-Item -Path (Join-Path $installerSourceDir "*") -Destination $installerInstallDir -Recurse -Force
}

Copy-MaintenanceScripts -PackageRoot $resolvedPackageRoot -StateDir $StateDir

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
    if (-not [string]::Equals([IO.Path]::GetFullPath($resolvedSettingsSource), [IO.Path]::GetFullPath($settingsDestination), [StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $resolvedSettingsSource -Destination $settingsDestination -Force
    }
} elseif ((Get-Item -LiteralPath $settingsDestination).Length -eq 0) {
    $templateSource = Join-Path $resolvedPackageRoot "agent.settings.template.json"
    if (-not (Test-Path $templateSource)) {
        throw "Template de configuracao nao encontrado: $templateSource"
    }

    Copy-Item -Path $templateSource -Destination (Join-Path $StateDir "agent.settings.json") -Force
}
Set-Acl -LiteralPath $settingsDestination -AclObject $secretAcl

$agentExeInstalled = Join-Path $InstallDir "WebstationBackup.Agent.Service.exe"
$publicSettings = Get-Content -LiteralPath $settingsDestination -Raw | ConvertFrom-Json |
    Select-Object ControlPlaneBaseUrl, CustomerId, HostId, AwsRegion, S3BucketName, S3KeyPrefix
$publicSettings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $StateDir 'agent.public.json') -Encoding UTF8
$displayVersion = Get-AgentDisplayVersion -ExecutablePath $agentExeInstalled
Ensure-ServiceInstalled `
    -Name $ServiceName `
    -DisplayName $ServiceDisplayName `
    -BinaryPath $agentExeInstalled `
    -ServiceStateDir $StateDir `
    -Credential $ServiceCredential
Ensure-ServiceRecovery -Name $ServiceName

Register-StartMenuShortcuts -InstallDir $InstallDir -StateDir $StateDir
Register-UninstallEntry -InstallDir $InstallDir -StateDir $StateDir -DisplayVersion $displayVersion
Write-InstallationMetadata -InstallDir $InstallDir -StateDir $StateDir -DisplayVersion $displayVersion

if ($StartService.IsPresent) {
    Start-Service -Name $ServiceName
    $service = Get-Service -Name $ServiceName
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))
    $service.Refresh()
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
        throw "O servico '$ServiceName' nao atingiu o estado Running apos a instalacao."
    }
}

Write-Host "Agent instalado com sucesso."
Write-Host ("Binarios: {0}" -f $InstallDir)
Write-Host ("Estado/configuracao: {0}" -f $StateDir)
Write-Host ("Versao instalada: {0}" -f $displayVersion)
Write-Host ("Atalhos do menu Iniciar: {0}" -f $startMenuFolder)

if (-not $StartService.IsPresent) {
    Write-Host "O servico nao foi iniciado automaticamente."
    Write-Host "Revise agent.settings.json e execute um dry-run antes de iniciar o servico."
}
