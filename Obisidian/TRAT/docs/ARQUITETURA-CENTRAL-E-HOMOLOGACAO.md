# Arquitetura central e ambientes

## Decisão de arquitetura

O TRAT utiliza um único ControlPlane central. Cada cliente é representado por um cadastro `Customer`; os servidores dos clientes recebem somente o Agent.

```text
Operadores Outbox Tech
        |
      HTTPS
        v
Nginx Proxy Manager
        |
 rede Docker privada
        v
ControlPlane ASP.NET Core -----> SQLite + chaves persistentes
        ^
        |
 HTTPS + token por cliente
        |
Agents Windows ----------------> AWS S3
```

Não deve ser criado um ControlPlane por cliente.

## Ambiente ativo do MVP

| Componente | Configuração |
| --- | --- |
| Branch | `development` |
| Stack | `trat-dev` |
| Container | `trat-dev` |
| Imagem | `ghcr.io/pedroaraujox/trat-controlplane:development` |
| URL pública | `https://trat-hml.outboxtech.com.br` |
| Rede do proxy | `nginx-proxy_default` |
| Subnet validada em 12/08/2026 | `172.19.0.0/16` |
| Origem do proxy | `http://trat-dev:8080` |

O nome histórico `hml` permanece no domínio, mas esta é a única stack remota usada para desenvolver e validar o MVP.

## Persistência

A stack cria dois volumes:

- `trat-dev_data`: banco SQLite e chaves do ASP.NET Data Protection;
- `trat-dev_backups`: cópias consistentes e automáticas do SQLite.

O nome exato pode variar conforme o prefixo aplicado pelo Portainer. Nunca remova volumes durante atualização ou diagnóstico comum.

## Rede e exposição

- a porta 8080 existe somente na rede Docker;
- o Nginx Proxy Manager é o único ponto de entrada público;
- o proxy encaminha `Host` e `X-Forwarded-*`;
- o ControlPlane confia somente na subnet configurada em `TRAT_PROXY_NETWORK_CIDR`;
- o healthcheck usa o hostname público para respeitar o filtro `AllowedHosts`.

## Banco e escala

SQLite é adequado para o piloto controlado, desde que:

- exista uma única réplica do ControlPlane;
- o arquivo permaneça em volume local persistente;
- backups sejam copiados para fora da VPS;
- restauração seja ensaiada periodicamente.

Alta disponibilidade e múltiplas réplicas exigirão reavaliar a persistência antes da implantação.

## Produção

A branch `production`, a tag `:production` e o hostname `trat.outboxtech.com.br` estão reservados. Nenhuma stack `trat-prod` está autorizada durante o MVP atual. A decisão futura exige aceite formal dos gates em [MVP-1-MES-PRONTIDAO.md](MVP-1-MES-PRONTIDAO.md).

## Alternativa Windows

Os scripts em `deploy/controlplane` continuam disponíveis para desenvolvimento legado, diagnóstico e cenários excepcionais. Eles não são o caminho de implantação da Contabo e não devem introduzir Cloudflare Tunnel na arquitetura atual.

## Decisões relacionadas

- [ADR-0001: ControlPlane central em Docker com SQLite](adr/0001-controlplane-central-docker-sqlite.md)
- [ADR-0002: MVP somente em development](adr/0002-mvp-somente-development.md)
