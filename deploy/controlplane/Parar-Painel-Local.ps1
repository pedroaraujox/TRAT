[CmdletBinding()]
param(
    [string]$PackageRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$packageRootPath = (Resolve-Path $PackageRoot).Path
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
