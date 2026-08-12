# TRAT

O TRAT é uma plataforma centralizada de orquestração e monitoramento de backups para AWS S3. Um único ControlPlane é operado pela Outbox Tech e cada servidor de cliente recebe somente o Agent Windows.

## Estado atual

O MVP está ativo exclusivamente no ambiente de desenvolvimento:

| Item | Valor atual |
| --- | --- |
| Branch operacional | `development` |
| Stack Portainer | `trat-dev` |
| Imagem | `ghcr.io/pedroaraujox/trat-controlplane:development` |
| URL | `https://trat-hml.outboxtech.com.br` |
| Hospedagem | Contabo + Docker + Portainer |
| Proxy HTTPS | Nginx Proxy Manager |
| Banco | SQLite em volume Docker persistente |
| Réplicas | 1 |

A branch `production` está reservada, mas nenhum ambiente de produção deve ser criado durante a fase atual do MVP.

## Arquitetura

```text
Operador -> HTTPS -> Nginx Proxy Manager -> trat-dev:8080
                                               |
                                               v
                                      SQLite persistente

Agent Windows -> HTTPS + token do cliente -> ControlPlane
      |
      v
AWS S3 com política sem exclusão
```

Regras fundamentais:

- o Agent nunca pode excluir objetos do S3;
- a porta 8080 não é publicada na VM;
- SQLite opera com uma única réplica;
- banco, chaves e backups ficam em volumes persistentes;
- segredos, bancos, logs e artefatos gerados não são versionados.

## Desenvolvimento local com Docker

```powershell
Copy-Item .env.example .env
# Defina uma senha local forte no arquivo .env.
docker compose up --build -d
docker compose ps
```

Acesse `http://localhost:5080`. Para parar sem apagar os dados:

```powershell
docker compose down
```

Nunca use `docker compose down --volumes` sem intenção explícita de apagar o banco local.

## Desenvolvimento nativo no Windows

```powershell
.\Liberar-TRAT.ps1
.\Iniciar-TRAT.ps1
.\Parar-TRAT.ps1
```

## Publicação do MVP

Todo push em `development` executa o GitHub Actions, gera o instalador do Agent e publica a imagem `:development`. Depois do workflow concluir, a atualização é aplicada no Portainer com **Pull and redeploy**, preservando os volumes.

## Documentação

Comece pelo [índice de documentação](docs/README.md). Os documentos operacionais principais são:

- [arquitetura e ambientes](docs/ARQUITETURA-CENTRAL-E-HOMOLOGACAO.md);
- [operação do MVP](docs/OPERACAO-MVP.md);
- [deploy no Portainer](deploy/portainer/README.md);
- [backup e restauração](docs/BACKUP-E-RESTAURACAO.md);
- [resposta a incidentes](docs/RESPOSTA-A-INCIDENTES.md);
- [segurança e segredos](docs/SEGURANCA-E-SEGREDOS.md);
- [fluxo Git e releases](docs/FLUXO-GIT-E-RELEASE.md);
- [checklist de desenvolvimento](docs/CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md).

## Licença e suporte

O repositório é público, mas isso não implica uma licença de uso. Até que um arquivo `LICENSE` seja definido pelo proprietário, todos os direitos permanecem reservados. Consulte [SECURITY.md](SECURITY.md) para reporte responsável de vulnerabilidades.
