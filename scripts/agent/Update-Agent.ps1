[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,
    [string]$InstallDir = "C:\Program Files\TRAT\Agent",
    [string]$StateDir = "C:\ProgramData\TRAT\Agent",
    [switch]$DoNotStartService
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

$legacyInstallDir = "C:\Program Files\WebstationBackup\Agent"
$legacyStateDir = "C:\ProgramData\WebstationBackup\Agent"
if (-not $MyInvocation.BoundParameters.ContainsKey("InstallDir") -and (Test-Path $legacyInstallDir) -and -not (Test-Path $InstallDir)) {
    $InstallDir = $legacyInstallDir
}
if (-not $MyInvocation.BoundParameters.ContainsKey("StateDir") -and (Test-Path $legacyStateDir) -and -not (Test-Path $StateDir)) {
    $StateDir = $legacyStateDir
}

$resolvedPackageRoot = (Resolve-Path $PackageRoot).Path
$installScriptPath = Join-Path $resolvedPackageRoot "Install-Agent.ps1"
$settingsPath = Join-Path $StateDir "agent.settings.json"

if (-not (Test-Path $installScriptPath)) {
    throw "Install-Agent.ps1 nao encontrado no pacote informado: $installScriptPath"
}

if (-not (Test-Path $settingsPath)) {
    throw "agent.settings.json nao encontrado em: $settingsPath"
}

$installArgs = @{
    PackageRoot = $resolvedPackageRoot
    InstallDir = $InstallDir
    StateDir = $StateDir
    SettingsSourcePath = $settingsPath
}

if (-not $DoNotStartService.IsPresent) {
    $installArgs["StartService"] = $true
}

& $installScriptPath @installArgs
