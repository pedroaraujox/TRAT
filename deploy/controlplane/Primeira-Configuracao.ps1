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

function Read-RequiredValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt,
        [string]$DefaultValue = ""
    )

    while ($true) {
        if ([string]::IsNullOrWhiteSpace($DefaultValue)) {
            $value = Read-Host $Prompt
        }
        else {
            $value = Read-Host ("{0} [{1}]" -f $Prompt, $DefaultValue)
            if ([string]::IsNullOrWhiteSpace($value)) {
                $value = $DefaultValue
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }

        Write-Host "Valor obrigatorio. Tente novamente." -ForegroundColor Yellow
    }
}

function Test-PasswordPolicy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Password
    )

    if ($Password.Length -lt 10) { return $false }
    if ($Password.ToCharArray() | Where-Object { [char]::IsWhiteSpace($_) } | Select-Object -First 1) { return $false }

    $hasLower = $Password.ToCharArray() | Where-Object { [char]::IsLower($_) } | Select-Object -First 1
    $hasUpper = $Password.ToCharArray() | Where-Object { [char]::IsUpper($_) } | Select-Object -First 1
    $hasDigit = $Password.ToCharArray() | Where-Object { [char]::IsDigit($_) } | Select-Object -First 1
    $hasSymbol = $Password.ToCharArray() | Where-Object { -not [char]::IsLetterOrDigit($_) } | Select-Object -First 1

    $categories = 0
    if ($hasLower) { $categories++ }
    if ($hasUpper) { $categories++ }
    if ($hasDigit) { $categories++ }
    if ($hasSymbol) { $categories++ }

    return $categories -ge 3
}

$packageRootPath = Resolve-PackageRootPath -PathValue $PackageRoot
$templatePath = Join-Path $packageRootPath "appsettings.Local.template.json"
$targetPath = Join-Path $packageRootPath "appsettings.Local.json"

if (-not (Test-Path $templatePath)) {
    throw "Template de configuracao nao encontrado em: $templatePath"
}

$adminEmail = Read-RequiredValue -Prompt "Email do administrador inicial" -DefaultValue "admin@trat.local"
$adminDisplayName = Read-RequiredValue -Prompt "Nome do administrador inicial" -DefaultValue "Administrador"
$adminPassword = Read-RequiredValue -Prompt "Senha do administrador inicial"
while (-not (Test-PasswordPolicy -Password $adminPassword)) {
    Write-Host "Senha fraca. Regras: minimo 10 caracteres, sem espacos, e ao menos 3 tipos (maiuscula/minuscula/numero/simbolo)." -ForegroundColor Yellow
    $adminPassword = Read-RequiredValue -Prompt "Senha do administrador inicial"
}

$tokenBytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $rng.GetBytes($tokenBytes)
}
finally {
    $rng.Dispose()
}

$adminToken = ([Convert]::ToBase64String($tokenBytes)).TrimEnd('=').Replace('+', 'A').Replace('/', 'B')

$configuration = [ordered]@{
    ControlPlane = [ordered]@{
        Security = [ordered]@{
            AdminToken = $adminToken
        }
        BootstrapAdmin = [ordered]@{
            Email = $adminEmail
            DisplayName = $adminDisplayName
            Password = $adminPassword
        }
        AgentDownloads = [ordered]@{
            ArtifactsRoot = "artifacts"
        }
        Database = [ordered]@{
            SqlitePath = "data/controlplane.db"
        }
        Seed = [ordered]@{
            EnableDemoData = $false
        }
    }
}

$content = $configuration | ConvertTo-Json -Depth 8
Set-Content -Path $targetPath -Value $content -Encoding UTF8

$dataDir = Join-Path $packageRootPath "data"
$logsDir = Join-Path $packageRootPath "logs"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null

Write-Host ""
Write-Host "Configuracao local criada com sucesso:" -ForegroundColor Green
Write-Host ("- Arquivo: {0}" -f $targetPath)
Write-Host ("- Banco SQLite: {0}" -f (Join-Path $packageRootPath "data\controlplane.db"))
Write-Host ("- Token administrativo para API: {0}" -f $adminToken)
Write-Host ""
Write-Host "Guarde o token administrativo em local seguro. Ele protege apenas as rotas /api/v1/admin." -ForegroundColor Yellow
