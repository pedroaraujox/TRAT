[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-ExistingPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    $candidate = $PathValue.Trim().Trim('"')
    if ($candidate.Length -gt 3) { $candidate = $candidate.TrimEnd('\') }
    return (Resolve-Path -LiteralPath $candidate).Path
}

function Copy-IfExists {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    if (Test-Path $SourcePath) {
        Copy-Item -Path $SourcePath -Destination $DestinationDir -Recurse -Force
    }
}

$packageRootPath = Resolve-ExistingPath -PathValue $PackageRoot
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$supportDir = Join-Path $packageRootPath ("support\trat-support-{0}" -f $timestamp)
New-Item -ItemType Directory -Force -Path $supportDir | Out-Null

Copy-IfExists -SourcePath (Join-Path $packageRootPath "appsettings.Local.json") -DestinationDir $supportDir
Copy-IfExists -SourcePath (Join-Path $packageRootPath "appsettings.json") -DestinationDir $supportDir
Copy-IfExists -SourcePath (Join-Path $packageRootPath "data\controlplane.db") -DestinationDir $supportDir
Copy-IfExists -SourcePath (Join-Path $packageRootPath "logs") -DestinationDir $supportDir

$agentStateCandidates = @(
    "C:\ProgramData\TRAT\Agent",
    "C:\ProgramData\WebstationBackup\Agent"
)
foreach ($candidate in $agentStateCandidates) {
    if (Test-Path $candidate) {
        $target = Join-Path $supportDir ("agent-state-" + (Split-Path $candidate -Leaf))
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Copy-IfExists -SourcePath (Join-Path $candidate "agent.settings.json") -DestinationDir $target
        Copy-IfExists -SourcePath (Join-Path $candidate "project.rules.json") -DestinationDir $target
        Copy-IfExists -SourcePath (Join-Path $candidate "agent.state.json") -DestinationDir $target
        Copy-IfExists -SourcePath (Join-Path $candidate "agent.log.jsonl") -DestinationDir $target
        Copy-IfExists -SourcePath (Join-Path $candidate "tray-error.log") -DestinationDir $target
        Copy-IfExists -SourcePath (Join-Path $candidate "installer-error.log") -DestinationDir $target
    }
}

$zipPath = $supportDir + ".zip"
Compress-Archive -Path (Join-Path $supportDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Coleta de evidencias concluida." -ForegroundColor Green
Write-Host ("- Pasta: {0}" -f $supportDir)
Write-Host ("- ZIP:   {0}" -f $zipPath)
