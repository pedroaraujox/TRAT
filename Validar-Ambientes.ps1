[CmdletBinding()]
param(
    [switch]$SkipLiveChecks
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$repoRoot = $PSScriptRoot

$profiles = @(
    @{ Name = "local"; Url = "http://localhost:5080"; Artifact = "artifacts\agent-package-local" },
    @{ Name = "hml"; Url = "https://trat-hml.outboxtech.com.br"; Artifact = "artifacts\agent-package-hml" }
)

foreach ($profile in $profiles) {
    $root = Join-Path $repoRoot $profile.Artifact
    $standalone = Join-Path $root "TRAT.Agent.Standalone"
    $manifestPath = Join-Path $standalone "agent-package.manifest.json"
    $setupPath = Join-Path $standalone "TRAT.Agent.Setup.exe"
    if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
        throw "Instalador ausente para $($profile.Name): $setupPath"
    }
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Manifesto ausente para $($profile.Name): $manifestPath"
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.environment -ne $profile.Name) {
        throw "Ambiente incorreto no manifesto de $($profile.Name): $($manifest.environment)"
    }
    if ($manifest.controlPlaneBaseUrl.TrimEnd('/') -ne $profile.Url) {
        throw "URL incorreta no manifesto de $($profile.Name): $($manifest.controlPlaneBaseUrl)"
    }

    $packageUrlPath = Join-Path $root "TRAT.Agent.Package\controlplane.url"
    $packageUrl = (Get-Content -LiteralPath $packageUrlPath -Raw).Trim().TrimEnd('/')
    if ($packageUrl -ne $profile.Url) {
        throw "controlplane.url incorreto para $($profile.Name): $packageUrl"
    }

    Write-Host "OK artifact $($profile.Name): $($profile.Url)" -ForegroundColor Green
}

if (-not $SkipLiveChecks) {
    foreach ($profile in $profiles) {
        $endpoint = $profile.Url + "/api/v1/environment"
        $response = Invoke-RestMethod -Uri $endpoint -TimeoutSec 20
        if ($response.name -ne $profile.Name) {
            throw "O endpoint $endpoint reportou ambiente '$($response.name)', esperado '$($profile.Name)'."
        }
        if ($response.publicUrl.TrimEnd('/') -ne $profile.Url) {
            throw "O endpoint $endpoint reportou URL '$($response.publicUrl)', esperada '$($profile.Url)'."
        }
        Write-Host "OK live $($profile.Name): $endpoint" -ForegroundColor Green
    }
}

Write-Host "Separacao de ambientes validada com sucesso." -ForegroundColor Green
