# Changelog

Mudanças relevantes serão registradas neste arquivo. O projeto ainda não adota releases semânticos formais.

## Unreleased

### Documentação

- consolidada a stack `trat-dev` como único ambiente remoto do MVP;
- documentados operação, segurança, backup, incidentes, observabilidade e fluxo Git;
- produção marcada explicitamente como não implantada;
- removidas orientações conflitantes de Contabo/Windows/Cloudflare.

## 2026-08-12

### Infraestrutura

- adicionado Docker Compose local validado;
- publicada imagem GHCR `:development`;
- criada stack `trat-dev` na Contabo;
- configurado acesso HTTPS pelo Nginx Proxy Manager;
- corrigido healthcheck para respeitar `AllowedHosts`;
- confirmados build, login, health e persistência local.
