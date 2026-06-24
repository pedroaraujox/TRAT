[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,
    [string]$ServiceName = "TRATControlPlane",
    [string]$ServiceDisplayName = "TRAT ControlPlane",
    [string]$Url = "http://localhost:5080"
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

function Resolve-ExistingPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    $candidate = $PathValue.Trim().Trim('"')
    if ($candidate.Length -gt 3) { $candidate = $candidate.TrimEnd('\') }
    return (Resolve-Path -LiteralPath $candidate).Path
}

Assert-Administrator

$packageRootPath = Resolve-ExistingPath -PathValue $PackageRoot
$exePath = Join-Path $packageRootPath "ControlPlane.Api.exe"
if (-not (Test-Path $exePath)) {
    throw "Executavel do painel nao encontrado em: $exePath"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    Write-Host "Servico ja existe. Atualizando configuracao..." -ForegroundColor Yellow
    sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 1
    sc.exe config $ServiceName start= auto | Out-Null
}
else {
    $binPath = ('"{0}" --urls "{1}"' -f $exePath, $Url)
    sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= ('"{0}"' -f $ServiceDisplayName) | Out-Null
}

sc.exe description $ServiceName "TRAT ControlPlane" | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null

sc.exe start $ServiceName | Out-Null

Write-Host "Servico instalado e iniciado com sucesso." -ForegroundColor Green
Write-Host ("- Nome: {0}" -f $ServiceName)
Write-Host ("- URL:  {0}" -f $Url)
