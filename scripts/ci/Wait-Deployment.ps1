[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PanelUrl,
    [Parameter(Mandatory)][ValidateSet('local','hml','production')][string]$Environment,
    [Parameter(Mandatory)][string]$Revision,
    [int]$TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    try {
        $identity = Invoke-RestMethod "$($PanelUrl.TrimEnd('/'))/api/v1/environment" -TimeoutSec 15
        $ready = Invoke-RestMethod "$($PanelUrl.TrimEnd('/'))/api/v1/readiness" -TimeoutSec 15
        if ($identity.name -eq $Environment -and $identity.publicUrl.TrimEnd('/') -eq $PanelUrl.TrimEnd('/') -and
            $identity.revision -eq $Revision -and $ready.revision -eq $Revision -and $ready.status -eq 'ready' -and $ready.agentPackageReady) {
            Write-Host "Deploy validado: $Environment, revisao $Revision, Agent $($ready.agentVersion)."
            return
        }
    } catch { Write-Host 'Aguardando ambiente, banco e pacote da revisao esperada...' }
    Start-Sleep -Seconds 10
} while ([DateTimeOffset]::UtcNow -lt $deadline)
throw "Deploy nao ficou pronto em $TimeoutSeconds segundos. Nao promover esta revisao."
