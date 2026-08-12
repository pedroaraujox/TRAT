[CmdletBinding()]
param(
    [string]$PublicUrl = "https://trat-hml.outboxtech.com.br"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$healthUrl = $PublicUrl.TrimEnd('/') + "/api/v1/health"
$health = Invoke-WebRequest -UseBasicParsing -Uri $healthUrl -TimeoutSec 20
if ($health.StatusCode -ne 200 -or $health.Content -notmatch '"status"\s*:\s*"ok"') {
    throw "Health check invalido em $healthUrl."
}

$login = Invoke-WebRequest -UseBasicParsing -Uri ($PublicUrl.TrimEnd('/') + "/login") -TimeoutSec 20 -SessionVariable session
if ($login.StatusCode -ne 200 -or $login.Content -notmatch 'TRAT') {
    throw "Tela de login indisponivel."
}

$requiredHeaders = @("X-Content-Type-Options", "X-Frame-Options", "Content-Security-Policy", "Referrer-Policy")
foreach ($headerName in $requiredHeaders) {
    if ([string]::IsNullOrWhiteSpace([string]$login.Headers[$headerName])) {
        throw "Cabecalho de seguranca ausente: $headerName"
    }
}

$adminApiStatus = $null
try {
    Invoke-WebRequest -UseBasicParsing -Uri ($PublicUrl.TrimEnd('/') + "/api/v1/admin/customers") -TimeoutSec 20 -ErrorAction Stop | Out-Null
    $adminApiStatus = 200
}
catch {
    if ($null -ne $_.Exception.Response) { $adminApiStatus = [int]$_.Exception.Response.StatusCode }
}
if ($adminApiStatus -ne 404) { throw "API administrativa deveria estar desativada, mas retornou HTTP $adminApiStatus." }

Write-Host "Homologacao publica validada com sucesso: $PublicUrl" -ForegroundColor Green
Write-Host "- HTTPS e health: OK"
Write-Host "- Login: OK"
Write-Host "- Cabecalhos de seguranca: OK"
Write-Host "- API administrativa desativada: OK"
