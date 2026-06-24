[CmdletBinding()]
param(
    [string]$ServiceName = "TRATControlPlane"
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

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -eq $existing) {
    Write-Host "Servico nao encontrado: $ServiceName" -ForegroundColor Yellow
    return
}

try {
    sc.exe stop $ServiceName | Out-Null
} catch {
}

Start-Sleep -Seconds 1
sc.exe delete $ServiceName | Out-Null

Write-Host "Servico removido com sucesso." -ForegroundColor Green
Write-Host ("- Nome: {0}" -f $ServiceName)
