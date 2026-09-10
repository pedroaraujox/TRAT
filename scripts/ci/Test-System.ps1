[CmdletBinding()]
param([switch]$IncludeAws, [string]$AwsTestBucket, [string]$AwsRegion = 'us-east-1')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $repo
try {
    foreach ($script in Get-ChildItem scripts -Filter '*.ps1' -Recurse) {
        $parseErrors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$null, [ref]$parseErrors) | Out-Null
        if ($parseErrors) { throw "PowerShell invalido: $($script.Name): $($parseErrors.Message -join '; ')" }
    }
    dotnet restore WebstationBackup.sln
    if ($LASTEXITCODE) { throw 'Restore falhou.' }
    dotnet build WebstationBackup.sln -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build falhou.' }
    foreach ($project in @('tests/ControlPlane.Api.Tests/ControlPlane.Api.Tests.csproj','tests/Agent.Tests/Agent.Tests.csproj')) {
        dotnet test $project -c Release --no-build --filter 'Category!=AWS' --logger trx --results-directory artifacts/mvp-validation
        if ($LASTEXITCODE) { throw "Testes falharam: $project" }
    }
    $audit = dotnet list WebstationBackup.sln package --vulnerable --include-transitive --format json
    if ($LASTEXITCODE) { throw 'Auditoria NuGet falhou.' }
    $audit | Set-Content artifacts/mvp-validation/dependencies.json
    $report = $audit | ConvertFrom-Json
    if ($audit -match '"vulnerabilities"\s*:') { throw 'Dependencias vulneraveis encontradas. Consulte dependencies.json.' }
    if ($IncludeAws) {
        if ([string]::IsNullOrWhiteSpace($AwsTestBucket)) { throw 'Informe um bucket dedicado em -AwsTestBucket.' }
        $env:TRAT_TEST_AWS_BUCKET = $AwsTestBucket
        $env:TRAT_TEST_AWS_REGION = $AwsRegion
        dotnet test tests/Agent.Tests/Agent.Tests.csproj -c Release --no-build --filter 'Category=AWS' --logger 'trx;LogFileName=aws.trx' --results-directory artifacts/mvp-validation
        if ($LASTEXITCODE) { throw 'Integracao AWS falhou.' }
    }
} finally { Pop-Location }
