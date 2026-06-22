[CmdletBinding()]
param(
    [string]$PackageRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-PackageRootPath {
    param(
        [string]$PathValue
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($PathValue)) { $PSScriptRoot } else { $PathValue.Trim().Trim('"') }
    if ($candidate.Length -gt 3) {
        $candidate = $candidate.TrimEnd('\')
    }

    return (Resolve-Path -LiteralPath $candidate).Path
}

$packageRootPath = Resolve-PackageRootPath -PathValue $PackageRoot
$pidPath = Join-Path $packageRootPath "controlplane.pid"

if (-not (Test-Path $pidPath)) {
    return
}

$pidValue = (Get-Content -Path $pidPath -Raw -Encoding ASCII).Trim()
if ([string]::IsNullOrWhiteSpace($pidValue)) {
    Remove-Item -Path $pidPath -Force -ErrorAction SilentlyContinue
    return
}

$processId = 0
if (-not [int]::TryParse($pidValue, [ref]$processId)) {
    Remove-Item -Path $pidPath -Force -ErrorAction SilentlyContinue
    return
}

$process = Get-Process -Id $processId -ErrorAction SilentlyContinue
if ($null -ne $process) {
    Stop-Process -Id $processId -Force -ErrorAction Stop
}

Remove-Item -Path $pidPath -Force -ErrorAction SilentlyContinue
