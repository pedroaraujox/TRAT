# Contabo e Portainer — stack do MVP

## Estado atual

Somente a stack de desenvolvimento está autorizada:

```text
Stack: trat-dev
Branch: refs/heads/development
Compose: deploy/portainer/compose.yml
Imagem: ghcr.io/pedroaraujox/trat-controlplane:development
URL: https://trat-hml.outboxtech.com.br
```

O repositório e a imagem GHCR são públicos. Não configure token Git ou registry privado enquanto essa visibilidade permanecer.

## Pré-requisitos

- Docker e Portainer operacionais na Contabo;
- Nginx Proxy Manager na rede `nginx-proxy_default`;
- DNS `trat-hml.outboxtech.com.br` apontando para a VM;
- portas 80 e 443 direcionadas ao Nginx Proxy Manager.

Rede validada em 12/08/2026:

```text
Nome: nginx-proxy_default
Subnet: 172.19.0.0/16
Gateway: 172.19.0.1
```

Confira novamente após qualquer recriação da rede.

## Criar a stack

Em **Stacks > Add stack > Git Repository**:

```text
Name: trat-dev
Repository URL: https://github.com/pedroaraujox/TRAT.git
Repository reference: refs/heads/development
Compose path: deploy/portainer/compose.yml
Authentication: desativada
Skip TLS verification: desativado
```

Variáveis:

```dotenv
TRAT_IMAGE=ghcr.io/pedroaraujox/trat-controlplane:development
TRAT_CONTAINER_NAME=trat-dev
TRAT_HOSTNAME=trat-hml.outboxtech.com.br
TRAT_PROXY_NETWORK=nginx-proxy_default
TRAT_PROXY_NETWORK_CIDR=172.19.0.0/16
TRAT_ADMIN_EMAIL=SEU_EMAIL
TRAT_ADMIN_NAME=Administrador TRAT - Desenvolvimento
TRAT_ADMIN_PASSWORD=UMA_SENHA_INICIAL_FORTE
```

Não registre valores reais em arquivos, tickets ou capturas.

## Resultado esperado

- `trat-dev-preparar-volumes-1`: encerra com código `0`;
- `trat-dev`: permanece `running` e `healthy`;
- nenhuma porta é publicada no host;
- o container recebe endereço na rede `nginx-proxy_default`;
- volumes `data` e `backups` permanecem após redeploy.

O primeiro container apenas ajusta permissões e deve encerrar.

## Nginx Proxy Manager

Crie um Proxy Host:

```text
Domain: trat-hml.outboxtech.com.br
Scheme: http
Forward hostname: trat-dev
Forward port: 8080
Access list: Publicly Accessible
Block Common Exploits: ativado
Websocket Support: ativado
Cache Assets: desativado
```

Solicite certificado Let's Encrypt, ative Force SSL e HTTP/2. Ative HSTS apenas depois de confirmar o HTTPS.

## Atualizar

1. aguarde o GitHub Actions concluir;
2. abra a stack `trat-dev`;
3. clique em **Pull and redeploy**;
4. preserve volumes;
5. aguarde `healthy`;
6. teste health, login e funcionalidade alterada.

Antes de acionar **Pull and redeploy**, compare as variáveis obrigatórias `${VAR:?mensagem}` do compose com as variáveis cadastradas no stack. Cadastre e salve qualquer variável nova antes do redeploy; o Portainer pode remover o container anterior antes de detectar uma interpolação ausente.

```text
https://trat-hml.outboxtech.com.br/api/v1/health
```

## Rollback

Tags com SHA são imutáveis. Para retornar:

1. identifique o último SHA aprovado no GitHub Actions;
2. faça backup antes de rollback que envolva schema;
3. troque temporariamente `TRAT_IMAGE` para `ghcr.io/pedroaraujox/trat-controlplane:<SHA>`;
4. faça redeploy preservando volumes;
5. valide health e dados;
6. registre o incidente.

Não faça downgrade de aplicação sobre banco migrado sem avaliar compatibilidade.

## Produção

`trat-prod` usa o mesmo Compose com `production.env.example` como referência, mas volumes, nome do container, hostname, senha e imagem próprios. Só faça o primeiro deploy ou atualização após a homologação do mesmo SHA, backup confirmado e aprovação da promoção `development -> production`.

## Runbooks relacionados

- [operação](../../docs/OPERACAO-MVP.md)
- [backup e restauração](../../docs/BACKUP-E-RESTAURACAO.md)
- [incidentes](../../docs/RESPOSTA-A-INCIDENTES.md)
