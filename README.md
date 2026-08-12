# TRAT

O TRAT e uma plataforma centralizada de orquestracao e monitoramento de backups para AWS S3.

## Arquitetura do produto

- um unico `ControlPlane` central, operado pela Outbox Tech e acessado por HTTPS;
- um `Agent` Windows instalado em cada servidor de cliente;
- o Agent recebe configuracao, executa o backup, grava no S3 e envia telemetria ao painel central;
- o Agent nunca possui permissao para excluir objetos do S3.

Os enderecos adotados sao `https://trat-hml.outboxtech.com.br` para homologacao e `https://trat.outboxtech.com.br` para producao, que somente deve ser ativada depois da aprovacao do piloto.

## Estado atual

O destino principal e uma VPS Contabo com Docker, Portainer e Nginx Proxy Manager. Homologacao e producao usam stacks e volumes isolados. A alternativa Windows com Cloudflare Tunnel continua documentada, mas nao e a arquitetura principal. Nenhum segredo de infraestrutura e versionado.

Gerar a pasta unica de homologacao:

```powershell
scripts\controlplane\Publish-Homologacao-Central.ps1
```

O ZIP para transferencia sera criado em `artifacts\trat-homologacao\TRAT.ControlPlane.Homologacao.zip`.

Documentos principais:

- `docs\ARQUITETURA-CENTRAL-E-HOMOLOGACAO.md`;
- `docs\COMPATIBILIDADE-WINDOWS.md`;
- `docs\CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md`;
- `docs\COMO-RODAR-LOCALMENTE.md`;
- `docs\MVP-1-MES-PRONTIDAO.md`.

Stack Portainer: `deploy\portainer\compose.yml` e `deploy\portainer\README.md`. A stack `trat-dev` acompanha a branch `development`; a stack `trat-prod` acompanha `production`, sempre com bancos, chaves e backups separados.

## Desenvolvimento local

### Docker Compose

Com o Docker Desktop instalado e iniciado:

```powershell
Copy-Item .env.example .env
# Edite a senha em .env antes de iniciar.
docker compose up --build -d
docker compose ps
```

Abra `http://localhost:5080`. Para acompanhar a inicializacao, use `docker compose logs -f controlplane`; para parar sem apagar o banco, use `docker compose down`. Nao use `down --volumes` a menos que queira excluir deliberadamente todo o estado local.

O Compose local e o arquivo `compose.yml` da raiz. A Contabo usa exclusivamente `deploy/portainer/compose.yml`; as duas configuracoes nao compartilham volumes nem segredos.

### Execucao nativa no Windows

```powershell
.\Liberar-TRAT.ps1
.\Iniciar-TRAT.ps1
.\Parar-TRAT.ps1
```

O modo local continua disponivel para desenvolvimento. Ele nao representa mais a arquitetura definitiva e nao deve ser replicado em cada servidor de cliente.
