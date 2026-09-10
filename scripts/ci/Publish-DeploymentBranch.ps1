[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('hml','production')][string]$Environment,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$Revision,
    [Parameter(Mandatory)][ValidatePattern('^sha256:[0-9a-f]{64}$')][string]$ImageDigest
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $repo
$previousIndex = $env:GIT_INDEX_FILE
try {
    $branch = "deploy-$Environment"
    $remote = git ls-remote origin "refs/heads/$branch"
    if ($LASTEXITCODE) { throw 'Falha ao consultar branch de deploy.' }
    $parent = $null
    if ($remote) {
        git fetch origin "refs/heads/$branch"
        if ($LASTEXITCODE) { throw 'Falha ao obter deploy anterior.' }
        $parent = (git rev-parse FETCH_HEAD).Trim()
    }
    $temp = Join-Path $repo ('artifacts/deploy-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    $compose = Get-Content deploy/portainer/compose.yml -Raw
    $compose = $compose.Replace('${TRAT_IMAGE:?Informe TRAT_IMAGE}', "ghcr.io/pedroaraujox/trat-controlplane@$ImageDigest")
    $file = Join-Path $temp 'compose.yml'
    [IO.File]::WriteAllText($file, $compose)
    $blob = (git hash-object -w -- $file).Trim()
    if ($LASTEXITCODE) { throw 'Falha ao gravar compose.' }
    $env:GIT_INDEX_FILE = Join-Path $temp 'index'
    git read-tree --empty
    if ($LASTEXITCODE) { throw 'Falha ao iniciar indice de deploy.' }
    git update-index --add --cacheinfo "100644,$blob,deploy/portainer/compose.yml"
    if ($LASTEXITCODE) { throw 'Falha ao registrar compose.' }
    $tree = (git write-tree).Trim()
    if ($LASTEXITCODE) { throw 'Falha ao criar arvore de deploy.' }
    $argsList = @('-c','user.name=TRAT deployment','-c','user.email=deployment@trat.local','commit-tree',$tree,'-m',"Deploy $Environment revision $Revision image $ImageDigest")
    if ($parent) { $argsList += @('-p',$parent) }
    $commit = (& git @argsList).Trim()
    if ($LASTEXITCODE) { throw 'Falha ao criar commit de deploy.' }
    git push origin "${commit}:refs/heads/$branch"
    if ($LASTEXITCODE) { throw 'Falha ao publicar branch de deploy; nenhuma atualizacao forcada permitida.' }
} finally { $env:GIT_INDEX_FILE = $previousIndex; Pop-Location }
