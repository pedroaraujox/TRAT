[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$WindowsRuntimeIdentifier = "win-x64",
    [string]$PanelUrl = "http://localhost:5080",
    [string]$AdminEmail = "",
    [string]$AdminPassword = "",
    [switch]$SkipAgentPublish,
    [switch]$SkipControlPlanePublish,
    [switch]$SkipSmokeTest,
    [switch]$SkipSetupDownload,
    [switch]$ResetLocalState,
    [switch]$NoPanelStart,
    [switch]$AllowIncompleteRelease
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$publishAgentScript = Join-Path $repoRoot "scripts\agent\Publish-Agent.ps1"
$publishControlPlaneScript = Join-Path $repoRoot "scripts\controlplane\Publish-ControlPlane.ps1"
$startScript = Join-Path $repoRoot "Iniciar-TRAT.ps1"
$stopScript = Join-Path $repoRoot "Parar-TRAT.ps1"
$validateScript = Join-Path $repoRoot "Validar-TRAT.ps1"
$packageRoot = Join-Path $repoRoot "artifacts\trat-local\TRAT.ControlPlane.Local"
$pidFile = Join-Path $packageRoot "controlplane.pid"
$serviceName = "TRATControlPlane"
$releaseReportsRoot = Join-Path $repoRoot "artifacts\release-reports"
$agentPackageDir = Join-Path $repoRoot "artifacts\agent-package-local\TRAT.Agent.Package"
$agentPackageZip = Join-Path $repoRoot "artifacts\agent-package-local\TRAT.Agent.Package.zip"
$controlPlanePackageZip = Join-Path $repoRoot "artifacts\trat-local\TRAT.ControlPlane.Local.zip"
$releaseStartedAtUtc = [DateTimeOffset]::UtcNow
$releaseReportId = $releaseStartedAtUtc.ToString("yyyyMMdd-HHmmss")

function Test-DotNetCliAvailable {
    try {
        $null = & dotnet --version
        return $LASTEXITCODE -eq 0
    }
    catch {
        return $false
    }
}

function Invoke-PowerShellScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,
        [string[]]$Arguments = @()
    )

    if (-not (Test-Path $ScriptPath)) {
        throw "Script nao encontrado: $ScriptPath"
    }

    Write-Host $Description -ForegroundColor Cyan
    $invocation = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $ScriptPath
    )
    if ($Arguments.Count -gt 0) {
        $invocation += $Arguments
    }

    & powershell.exe @invocation
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao executar: $ScriptPath"
    }
}

function Get-GitContext {
    $result = [ordered]@{
        Available = $false
        Branch = $null
        Commit = $null
        WorkingTreeDirty = $null
        Status = @()
    }

    try {
        $null = & git --version 2>$null
        if ($LASTEXITCODE -ne 0) {
            return $result
        }

        $result.Available = $true

        $branch = & git rev-parse --abbrev-ref HEAD 2>$null
        if ($LASTEXITCODE -eq 0) {
            $result.Branch = (($branch | Out-String).Trim())
        }

        $commit = & git rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0) {
            $result.Commit = (($commit | Out-String).Trim())
        }

        $statusLines = @(& git status --short 2>$null)
        if ($LASTEXITCODE -eq 0) {
            $normalized = @($statusLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            $result.Status = $normalized
            $result.WorkingTreeDirty = $normalized.Count -gt 0
        }
    }
    catch {
    }

    return $result
}

function Get-PathSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    $snapshot = [ordered]@{
        Path = $PathValue
        Exists = Test-Path $PathValue
        Type = "missing"
        SizeBytes = $null
        LastWriteTimeUtc = $null
        ItemCount = $null
    }

    if (-not $snapshot.Exists) {
        return $snapshot
    }

    $item = Get-Item -LiteralPath $PathValue -ErrorAction Stop
    $snapshot.LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString("o")

    if ($item.PSIsContainer) {
        $snapshot.Type = "directory"
        $children = @(Get-ChildItem -LiteralPath $PathValue -Recurse -Force -ErrorAction SilentlyContinue)
        $snapshot.ItemCount = $children.Count
    }
    else {
        $snapshot.Type = "file"
        $snapshot.SizeBytes = $item.Length
    }

    return $snapshot
}

function Convert-ReleaseReportToMarkdown {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Report,
        [Parameter(Mandatory = $true)]
        [string]$JsonPath
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $null = $lines.Add("# Release Report TRAT")
    $null = $lines.Add("")
    $null = $lines.Add(("- Status: {0}" -f $Report.ReleaseStatus))
    $null = $lines.Add(("- Inicio (UTC): {0}" -f $Report.StartedAtUtc))
    $null = $lines.Add(("- Fim (UTC): {0}" -f $Report.CompletedAtUtc))
    $null = $lines.Add(("- Painel: {0}" -f $Report.Environment.PanelUrl))
    $null = $lines.Add(("- Report JSON: {0}" -f $JsonPath))
    $null = $lines.Add("")
    $null = $lines.Add("## Publish")
    $null = $lines.Add("")
    $null = $lines.Add(("- Agent: {0} (executado={1})" -f $Report.Publish.Agent.Status, $Report.Publish.Agent.Executed))
    $null = $lines.Add(("- ControlPlane: {0} (executado={1})" -f $Report.Publish.ControlPlane.Status, $Report.Publish.ControlPlane.Executed))
    $null = $lines.Add("")
    $null = $lines.Add("## Smoke Test")
    $null = $lines.Add("")
    $null = $lines.Add(("- Status: {0}" -f $Report.SmokeTest.Status))
    if (-not [string]::IsNullOrWhiteSpace([string]$Report.SmokeTest.Reason)) {
        $null = $lines.Add(("- Motivo: {0}" -f $Report.SmokeTest.Reason))
    }
    $null = $lines.Add("")
    $null = $lines.Add("## Artifacts")
    $null = $lines.Add("")

    foreach ($entry in $Report.Artifacts.GetEnumerator()) {
        $artifact = $entry.Value
        $null = $lines.Add(("- {0}: exists={1}; type={2}; size={3}; items={4}; updated={5}" -f
            $entry.Key,
            $artifact.Exists,
            $artifact.Type,
            $artifact.SizeBytes,
            $artifact.ItemCount,
            $artifact.LastWriteTimeUtc))
    }

    if ($Report.Notes.Count -gt 0) {
        $null = $lines.Add("")
        $null = $lines.Add("## Observacoes")
        $null = $lines.Add("")
        foreach ($note in $Report.Notes) {
            $null = $lines.Add(("- {0}" -f $note))
        }
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$Report.Error)) {
        $null = $lines.Add("")
        $null = $lines.Add("## Erro")
        $null = $lines.Add("")
        $errorBlock = @(
            '```text'
            [string]$Report.Error
            '```'
        ) -join [Environment]::NewLine
        $null = $lines.Add($errorBlock)
    }

    return ($lines -join [Environment]::NewLine) + [Environment]::NewLine
}

function Save-ReleaseReport {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot,
        [Parameter(Mandatory = $true)]
        [string]$ReportId,
        [Parameter(Mandatory = $true)]
        [hashtable]$Report
    )

    New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

    $jsonPath = Join-Path $OutputRoot ("TRAT-release-{0}.json" -f $ReportId)
    $mdPath = Join-Path $OutputRoot ("TRAT-release-{0}.md" -f $ReportId)

    ($Report | ConvertTo-Json -Depth 8) | Set-Content -Path $jsonPath -Encoding UTF8
    (Convert-ReleaseReportToMarkdown -Report $Report -JsonPath $jsonPath) | Set-Content -Path $mdPath -Encoding UTF8

    return [ordered]@{
        JsonPath = $jsonPath
        MarkdownPath = $mdPath
    }
}

function Test-PanelReachable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url
    )

    try {
        $response = Invoke-WebRequest -Uri ($Url.TrimEnd('/') + "/login") -UseBasicParsing -TimeoutSec 10
        return $response.StatusCode -eq 200
    }
    catch {
        return $false
    }
}

function Wait-PanelReady {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [int]$TimeoutSeconds = 90
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-PanelReachable -Url $Url) {
            return
        }

        Start-Sleep -Seconds 3
    }

    throw "Painel nao respondeu em tempo habil em: $Url"
}

function Stop-TRATIfRunning {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    $hasServiceRunning = $null -ne $service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped
    $hasLocalPid = Test-Path $pidFile

    if (-not $hasServiceRunning -and -not $hasLocalPid) {
        return
    }

    Invoke-PowerShellScript `
        -Description "Parando instancia atual do TRAT antes da liberacao..." `
        -ScriptPath (Join-Path $RepoRoot "Parar-TRAT.ps1")
}

$releaseReport = [ordered]@{
    Product = "TRAT"
    ReleaseId = $releaseReportId
    StartedAtUtc = $releaseStartedAtUtc.ToString("o")
    CompletedAtUtc = $null
    ReleaseStatus = "RUNNING"
    Parameters = [ordered]@{
        Configuration = $Configuration
        WindowsRuntimeIdentifier = $WindowsRuntimeIdentifier
        PanelUrl = $PanelUrl
        SkipAgentPublish = [bool]$SkipAgentPublish
        SkipControlPlanePublish = [bool]$SkipControlPlanePublish
        SkipSmokeTest = [bool]$SkipSmokeTest
        SkipSetupDownload = [bool]$SkipSetupDownload
        ResetLocalState = [bool]$ResetLocalState
        NoPanelStart = [bool]$NoPanelStart
        AllowIncompleteRelease = [bool]$AllowIncompleteRelease
    }
    Environment = [ordered]@{
        RepoRoot = $repoRoot
        MachineName = $env:COMPUTERNAME
        UserName = $env:USERNAME
        PanelUrl = $PanelUrl
    }
    Git = Get-GitContext
    Publish = [ordered]@{
        Agent = [ordered]@{
            Requested = -not $SkipAgentPublish
            Executed = $false
            Status = if ($SkipAgentPublish) { "SKIPPED" } else { "PENDING" }
        }
        ControlPlane = [ordered]@{
            Requested = -not $SkipControlPlanePublish
            Executed = $false
            Status = if ($SkipControlPlanePublish) { "SKIPPED" } else { "PENDING" }
        }
    }
    SmokeTest = [ordered]@{
        Requested = -not $SkipSmokeTest
        Executed = $false
        Status = if ($SkipSmokeTest) { "SKIPPED" } else { "PENDING" }
        Reason = $null
    }
    Artifacts = [ordered]@{}
    Notes = New-Object System.Collections.Generic.List[string]
    Error = $null
}

$pendingError = $null
$savedReportPaths = $null

try {
    if ((-not $SkipAgentPublish -or -not $SkipControlPlanePublish) -and -not (Test-DotNetCliAvailable)) {
        throw "O dotnet SDK nao esta disponivel nesta maquina para rebuild/publish dos artefatos."
    }

    if (-not $SkipAgentPublish) {
        Invoke-PowerShellScript `
            -Description "Gerando artifacts do Agent..." `
            -ScriptPath $publishAgentScript `
            -Arguments @(
                "-EnvironmentProfile", "local",
                "-Configuration", $Configuration,
                "-WindowsRuntimeIdentifier", $WindowsRuntimeIdentifier
            )
        $releaseReport.Publish.Agent.Executed = $true
        $releaseReport.Publish.Agent.Status = "DONE"
    }
    else {
        $releaseReport.Notes.Add("Publish do Agent foi pulado por parametro.")
    }

    if (-not $SkipControlPlanePublish) {
        Stop-TRATIfRunning -RepoRoot $repoRoot
        Invoke-PowerShellScript `
            -Description "Gerando pacote local do ControlPlane..." `
            -ScriptPath $publishControlPlaneScript `
            -Arguments @(
                "-EnvironmentProfile", "local",
                "-Configuration", $Configuration,
                "-WindowsRuntimeIdentifier", $WindowsRuntimeIdentifier
            )
        $releaseReport.Publish.ControlPlane.Executed = $true
        $releaseReport.Publish.ControlPlane.Status = "DONE"
    }
    else {
        $releaseReport.Notes.Add("Publish do ControlPlane foi pulado por parametro.")
    }

    if ($SkipSmokeTest) {
        $releaseReport.ReleaseStatus = "INCOMPLETE"
        $releaseReport.SmokeTest.Reason = "Smoke test foi pulado por parametro."
        $releaseReport.Notes.Add("Liberacao incompleta: smoke test nao executado.")
    }
    else {
        $mustRestartPanel = $ResetLocalState -or -not (Test-PanelReachable -Url $PanelUrl)
        if ($mustRestartPanel) {
            if ($NoPanelStart) {
                throw "O smoke test exige o painel ativo. Remova -NoPanelStart ou inicie o painel manualmente antes da validacao."
            }

            Stop-TRATIfRunning -RepoRoot $repoRoot

            $startArguments = @("-NoBrowser")
            if ($ResetLocalState) {
                $startArguments += "-ResetLocalState"
            }

            Invoke-PowerShellScript `
                -Description "Iniciando o TRAT para smoke test..." `
                -ScriptPath $startScript `
                -Arguments $startArguments
        }

        Wait-PanelReady -Url $PanelUrl

        $validateArguments = @("-PanelUrl", $PanelUrl)
        if (-not [string]::IsNullOrWhiteSpace($AdminEmail)) {
            $validateArguments += @("-AdminEmail", $AdminEmail)
        }
        if (-not [string]::IsNullOrWhiteSpace($AdminPassword)) {
            $validateArguments += @("-AdminPassword", $AdminPassword)
        }
        if ($SkipSetupDownload) {
            $validateArguments += "-SkipSetupDownload"
        }

        Invoke-PowerShellScript `
            -Description "Executando smoke test do painel e dos downloads..." `
            -ScriptPath $validateScript `
            -Arguments $validateArguments

        $releaseReport.SmokeTest.Executed = $true
        $releaseReport.SmokeTest.Status = "PASSED"
        $releaseReport.ReleaseStatus = "COMPLETED"
    }
}
catch {
    $pendingError = $_
    $releaseReport.ReleaseStatus = "FAILED"
    $releaseReport.SmokeTest.Status = if ($releaseReport.SmokeTest.Executed) { $releaseReport.SmokeTest.Status } elseif ($SkipSmokeTest) { "SKIPPED" } else { "FAILED" }
    $releaseReport.Error = $_.Exception.Message
}
finally {
    $releaseReport.CompletedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    $releaseReport.Artifacts = [ordered]@{
        AgentPackageDir = Get-PathSnapshot -PathValue $agentPackageDir
        AgentPackageZip = Get-PathSnapshot -PathValue $agentPackageZip
        ControlPlanePackageDir = Get-PathSnapshot -PathValue $packageRoot
        ControlPlanePackageZip = Get-PathSnapshot -PathValue $controlPlanePackageZip
    }

    $savedReportPaths = Save-ReleaseReport -OutputRoot $releaseReportsRoot -ReportId $releaseReportId -Report $releaseReport
    Write-Host ("Release report JSON: {0}" -f $savedReportPaths.JsonPath) -ForegroundColor Cyan
    Write-Host ("Release report Markdown: {0}" -f $savedReportPaths.MarkdownPath) -ForegroundColor Cyan
}

if ($null -ne $pendingError) {
    throw $pendingError
}

if ($releaseReport.ReleaseStatus -eq "INCOMPLETE" -and -not $AllowIncompleteRelease) {
    throw ("Liberacao incompleta bloqueada. Rode novamente sem pular etapas criticas ou use -AllowIncompleteRelease conscientemente. Report: {0}" -f $savedReportPaths.MarkdownPath)
}

if ($releaseReport.ReleaseStatus -eq "INCOMPLETE") {
    Write-Host "Liberacao concluida em modo incompleto por parametro." -ForegroundColor Yellow
}
else {
    Write-Host "Fluxo de liberacao do TRAT concluido com sucesso." -ForegroundColor Green
}

Write-Host ("- Agent publish: {0}" -f (-not $SkipAgentPublish))
Write-Host ("- ControlPlane publish: {0}" -f (-not $SkipControlPlanePublish))
Write-Host ("- Smoke test executado: {0}" -f (-not $SkipSmokeTest))
Write-Host ("- Status final: {0}" -f $releaseReport.ReleaseStatus)
Write-Host ("- Painel validado em: {0}" -f $PanelUrl)
