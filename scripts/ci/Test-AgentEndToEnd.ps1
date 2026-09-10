[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PanelUrl,
    [Parameter(Mandatory)][pscredential]$AdminCredential,
    [Parameter(Mandatory)][string]$AgentPackage,
    [Parameter(Mandatory)][string]$AwsTestBucket,
    [string]$AwsRegion = 'us-east-1'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$base = $PanelUrl.TrimEnd('/')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$runId = 'mvp-' + [guid]::NewGuid().ToString('N').Substring(0, 16)
$root = Join-Path $repo "artifacts/e2e/$runId"
$source = Join-Path $root 'source'
$state = Join-Path $root 'state'
New-Item -ItemType Directory -Force -Path $source,$state,(Join-Path $source 'nested') | Out-Null
# A private fixture directory keeps enrollment material out of ordinary users' reach.
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
Set-Acl -LiteralPath $root -AclObject $acl
[IO.File]::WriteAllText((Join-Path $source 'sample.txt'), "TRAT synthetic root $runId")
[IO.File]::WriteAllText((Join-Path $source 'nested/sample.txt'), "TRAT synthetic nested $runId")
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
function Get-Token([string]$html) {
    $match = [regex]::Match($html,'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if (-not $match.Success) { throw 'Antiforgery ausente.' }
    [Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
}
function Post-Form([string]$url,[hashtable]$fields) {
    $page = Invoke-WebRequest "$base$url" -WebSession $session
    $fields['__RequestVerificationToken'] = Get-Token $page.Content
    $response = Invoke-WebRequest "$base$url" -Method Post -Body $fields -WebSession $session
    if ($response.Content -match '<div class="error-banner">') { throw "Formulario recusado: $url" }
    return $response
}
$login = Post-Form '/login' @{ email=$AdminCredential.UserName; password=$AdminCredential.GetNetworkCredential().Password }
if ($login.Content -match 'name="password"') { throw 'Login falhou.' }
$awsAccount = (aws sts get-caller-identity --query Account --output text).Trim()
if ($LASTEXITCODE) { throw 'STS falhou.' }
$customer = Post-Form '/admin/customers/new' @{Id=$runId;Name="Validacao sintetica $runId";AwsAccountId=$awsAccount}
$match = [regex]::Match($customer.Content,'<input type="text" value="([^"]+)" readonly')
if (-not $match.Success) { throw 'Token inicial do cliente nao retornado.' }
$token = [Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
$hostId = "$runId-host"
$headers = @{'X-Agent-Token'=$token}
$enroll = Invoke-RestMethod "$base/api/v1/agents/enroll" -Method Post -Headers $headers -ContentType application/json -Body (@{HostId=$hostId;Hostname=$hostId;OsVersion='Windows synthetic validation'}|ConvertTo-Json)
if ($enroll.customerId -ne $runId -or $enroll.hostId -ne $hostId) { throw 'Enroll divergiu da identidade esperada.' }
$prefix = "trat-validation/$runId"
$policy = Post-Form '/admin/policies/new' @{
    Id="$runId-policy";CustomerId=$runId;HostId=$hostId;Name="Validacao $runId";PolicyKind='operational';ScopeType='host';
    IncludePathsCsv=$source;AwsRegion=$AwsRegion;S3BucketName=$AwsTestBucket;S3KeyPrefix=$prefix;
    ScheduleDaysCsv='SUN';ScheduleDays='SUN';StartTimeLocal='23:59';MaxRuntimeMinutes='5';CpuLimitPercent='35';NetworkLimitMbit='10';Enabled='true'
}
$settingsPath = Join-Path $state 'agent.settings.json'
@{CustomerId=$runId;HostId=$hostId;ControlPlaneBaseUrl=$base;AgentToken=$token;IncludePaths=@();ExcludePaths=@()} |
    ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8
$agent = Join-Path ([IO.Path]::GetFullPath($AgentPackage)) 'bin/WebstationBackup.Agent.Service.exe'
$rules = Join-Path $repo 'project.rules.json'
foreach ($mode in @('dry-run','real')) {
    $arguments = @('--console','--settings',$settingsPath,'--state-dir',$state,'--rules',$rules)
    if ($mode -eq 'dry-run') { $arguments += '--dry-run' }
    & $agent @arguments
    if ($LASTEXITCODE) { throw "Agent falhou em $mode. Estado: $state" }
    $jobState = Get-Content (Join-Path $state 'agent.state.json') -Raw|ConvertFrom-Json
    if ($jobState.LastFinalState -ne 'SUCCEEDED') { throw "Job $mode nao concluiu." }
    $job = Invoke-WebRequest "$base/admin/jobs/$($jobState.LastJobId)" -WebSession $session
    if ($job.Content -notmatch 'SUCCEEDED') { throw 'Relatorio final nao confirmado no painel.' }
    Write-Host "OK $mode: job $($jobState.LastJobId) confirmado no painel."
}
$manifest = Get-Content (Join-Path $state "manifest.$($jobState.LastJobId).json") -Raw|ConvertFrom-Json
if ($manifest.PlannedItems -ne 2) { throw 'Manifesto nao contem os dois arquivos sinteticos.' }
foreach ($item in $manifest.Items) {
    $key = "$prefix/$($item.RelativePath)"
    $headRaw = aws s3api head-object --bucket $AwsTestBucket --key $key --checksum-mode ENABLED --region $AwsRegion --output json
    if ($LASTEXITCODE) { throw 'HeadObject falhou.' }
    $head = $headRaw|ConvertFrom-Json
    if ($head.ChecksumSHA256 -ne $item.Sha256Base64 -or $head.ContentLength -ne $item.SizeBytes) { throw 'Integridade final S3 divergente.' }
}
@{runId=$runId;panel=$base;jobId=$jobState.LastJobId;items=$manifest.PlannedItems;bytes=$manifest.PlannedBytes;prefix=$prefix;status='passed';validatedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')}|
    ConvertTo-Json|Set-Content (Join-Path $root 'result.json')
Write-Host "E2E aprovado: onboarding, politica, dry-run, Agent real, relatorio e checksum S3. Evidencia: $root/result.json"
