# Como rodar o TRAT localmente

## Opção recomendada: Docker Compose

### Pré-requisitos

- Windows 10/11 com WSL 2;
- Docker Desktop iniciado em containers Linux;
- Git;
- porta 5080 disponível.

### Primeira execução

```powershell
git clone https://github.com/pedroaraujox/TRAT.git
cd TRAT
git switch development
Copy-Item .env.example .env
```

Edite `.env` e defina uma senha local exclusiva. Depois execute:

```powershell
docker compose up --build -d
docker compose ps
```

Acesse:

- painel: `http://localhost:5080/login`;
- health: `http://localhost:5080/api/v1/health`.

### Comandos diários

```powershell
docker compose up --build -d
docker compose logs --tail 100 controlplane
docker compose restart controlplane
docker compose down
```

`docker compose down` preserva os volumes. `docker compose down --volumes` apaga banco, chaves e backups locais e não deve fazer parte do fluxo normal.

### Dados locais

| Dado | Volume |
| --- | --- |
| SQLite e chaves | `trat-local_trat_local_data` |
| Backups | `trat-local_trat_local_backups` |

Para listar:

```powershell
docker volume ls --filter name=trat-local
```

## Opção nativa no Windows

Use esta opção para validar scripts, instalador e serviço Windows:

### Pré-requisitos

- .NET 8 SDK;
- .NET Framework 4.8 Developer Pack;
- PowerShell 5.1;
- Windows x64.

### Fluxo completo

```powershell
.\Liberar-TRAT.ps1
.\Iniciar-TRAT.ps1
.\Validar-TRAT.ps1
.\Parar-TRAT.ps1
```

Artefatos ficam em `artifacts/` e não são versionados.

## Testes automatizados

```powershell
dotnet test tests\ControlPlane.Api.Tests\ControlPlane.Api.Tests.csproj -c Release
```

Compilação rápida do painel:

```powershell
dotnet build src\ControlPlane\ControlPlane.Api\ControlPlane.Api.csproj -c Release
```

## Troubleshooting

### Docker não é reconhecido

Reabra o terminal depois de instalar o Docker Desktop. Em instalação por usuário, a CLI pode estar em:

```text
%LOCALAPPDATA%\Programs\DockerDesktop\resources\bin\docker.exe
```

### Container `unhealthy`

```powershell
docker inspect trat-local --format "{{json .State.Health}}"
docker compose logs --tail 100 controlplane
```

### Porta 5080 ocupada

```powershell
Get-NetTCPConnection -LocalPort 5080
```

Altere `TRAT_LOCAL_PORT` no `.env` ou encerre conscientemente o processo conflitante.

### Downloads do Agent indisponíveis

O instalador precisa existir em `artifacts/agent-package` antes do build local:

```powershell
scripts\agent\Publish-Agent.ps1 -ControlPlaneBaseUrl "http://localhost:5080"
docker compose up --build -d
```

## Relação com a VM

O `compose.yml` da raiz é somente local. A Contabo usa `deploy/portainer/compose.yml`. Nunca copie o `.env` local para a VM e nunca compartilhe volumes entre esses ambientes.
