# ADR-0003 — Promoção local, homologação e produção

- Data: 2026-08-14
- Estado: aceita

## Contexto

O TRAT precisa funcionar de forma isolada em desenvolvimento local, homologação equivalente à produção e produção. Incidentes anteriores mostraram que compilar localmente não garante que o instalador ou a imagem pública estejam atualizados.

## Decisão

Adotar o fluxo obrigatório:

`local` → `development`/`trat-dev`/`trat-hml.outboxtech.com.br` → merge revisado → `production`/`trat-prod`/`trat.outboxtech.com.br`.

Cada ambiente possui URL, manifesto do Agent, banco SQLite, volumes, tokens, senhas e credenciais isolados. A revisão Git implantada deve ser observável pelo endpoint `/api/v1/environment`.

Produção recebe somente o mesmo commit aprovado em homologação. O deploy preserva volumes e possui rollback por tag SHA.

## Consequências

- nenhum artefato pode herdar silenciosamente a URL de outro ambiente;
- o pipeline testa antes de publicar;
- homologação é um gate técnico, não uma tag informal;
- produção pode existir, mas nunca recebe mudanças diretas ou não homologadas.
