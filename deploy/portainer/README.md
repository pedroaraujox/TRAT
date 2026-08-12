# Publicacao na Contabo via Portainer

O mesmo `compose.yml` e usado por duas stacks independentes. Cada stack le uma branch diferente do repositorio privado e usa uma imagem Docker com a mesma identificacao da branch:

| Ambiente | Stack | Branch/Reference | Imagem |
| --- | --- | --- | --- |
| Desenvolvimento | `trat-dev` | `refs/heads/development` | `ghcr.io/pedroaraujox/trat-controlplane:development` |
| Producao | `trat-prod` | `refs/heads/production` | `ghcr.io/pedroaraujox/trat-controlplane:production` |

O Portainer prefixa os volumes com o nome da stack. Assim, banco SQLite, chaves de sessao e backups ficam isolados mesmo usando o mesmo Compose.

## Pre-requisitos uma unica vez

1. Em **Registries**, cadastre `ghcr.io` usando um PAT do GitHub com somente `read:packages`.
2. Confirme em **Networks** o nome e o CIDR da rede do Nginx Proxy Manager.
3. Garanta que o repositorio privado possa ser lido pelo Portainer, usando credencial Git somente de leitura.
4. Nao publique a porta 8080 no host. O acesso externo deve passar apenas pelo Nginx Proxy Manager.

## Como criar cada stack pelo repositorio Git

Em **Stacks > Add stack > Repository**, preencha:

- Repository URL: `https://github.com/pedroaraujox/TRAT.git`;
- Repository reference: a reference indicada na tabela acima;
- Compose path: `deploy/portainer/compose.yml`;
- Authentication: habilitada, com credencial Git somente de leitura;
- Environment variables: copie o arquivo `.env.example` correspondente e troque todos os placeholders.

Nao marque para recriar volumes. Uma atualizacao da stack deve trocar o container e preservar `data` e `backups`.

## Desenvolvimento

Use os valores de `development.env.example`. O hostname publico continua com o sufixo `hml` para deixar claro que nao e producao.

```dotenv
TRAT_IMAGE=ghcr.io/pedroaraujox/trat-controlplane:development
TRAT_CONTAINER_NAME=trat-dev
TRAT_HOSTNAME=trat-hml.outboxtech.com.br
TRAT_PROXY_NETWORK=nginx-proxy_default
TRAT_PROXY_NETWORK_CIDR=172.19.0.0/16
TRAT_ADMIN_EMAIL=SEU_EMAIL
TRAT_ADMIN_NAME=Administrador TRAT
TRAT_ADMIN_PASSWORD=UMA_SENHA_INICIAL_FORTE
```

O CIDR `172.19.0.0/16` foi conferido em **Networks > nginx-proxy_default** no Portainer em 11/08/2026. Confira novamente antes da publicacao caso a rede seja recriada; nao use um intervalo mais amplo.

No Nginx Proxy Manager, crie um Proxy Host:

- Domain: `trat-hml.outboxtech.com.br`;
- Scheme: `http`;
- Forward Hostname: `trat-dev`;
- Forward Port: `8080`;
- SSL: certificado Let's Encrypt, Force SSL, HTTP/2 e HSTS.

Crie/ajuste o registro DNS `A` do subdominio para o IP publico da Contabo. Nao publique a porta 8080 no host.

Depois do primeiro login bem-sucedido, remova `TRAT_ADMIN_PASSWORD` do ambiente e substitua temporariamente no compose por um valor aleatorio descartavel caso o Portainer exija a variavel. O bootstrap nao altera a senha de um administrador ja existente.

## Producao

Somente apos o aceite do piloto, crie outra stack chamada `trat-prod`:

```dotenv
TRAT_IMAGE=ghcr.io/pedroaraujox/trat-controlplane:production
TRAT_CONTAINER_NAME=trat-prod
TRAT_HOSTNAME=trat.outboxtech.com.br
TRAT_PROXY_NETWORK=nginx-proxy_default
TRAT_PROXY_NETWORK_CIDR=172.19.0.0/16
TRAT_ADMIN_EMAIL=SEU_EMAIL
TRAT_ADMIN_NAME=Administrador TRAT
TRAT_ADMIN_PASSWORD=OUTRA_SENHA_INICIAL_FORTE
```

No Nginx Proxy Manager, aponte `trat.outboxtech.com.br` para `trat-prod:8080`. Desenvolvimento e producao nunca devem compartilhar volumes, banco, senha inicial ou tokens de clientes.

## Publicacao e atualizacao

A automacao do GitHub publica a imagem `development` a partir da branch `development` e a imagem `production` a partir da branch homonima. A stack de producao somente podera ser criada depois que os arquivos de deploy forem promovidos para `production`.

Para atualizar, use **Pull latest image and redeploy** ou habilite o mecanismo de atualizacao Git do Portainer depois do primeiro deploy manual validado. Mantenha uma unica replica porque o ControlPlane usa SQLite. O backup interno fica no volume `backups`; copie-o periodicamente para armazenamento fora da VPS, pois um volume local nao protege contra perda da Contabo.

## Ordem segura de implantacao

1. Fazer commit e push destas configuracoes em `development`.
2. Confirmar que o GitHub Actions publicou a imagem `:development`.
3. Criar apenas a stack `trat-dev`, configurar o proxy e validar login, persistencia e backup.
4. Promover `development` para `production` por pull request.
5. Confirmar a imagem `:production` e somente entao criar `trat-prod`.
