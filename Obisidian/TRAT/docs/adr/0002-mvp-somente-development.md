# ADR-0002 — MVP somente no ambiente development

- Data: 2026-08-12
- Estado: substituída pela ADR-0003

## Contexto

O TRAT entrou na fase de piloto e ainda precisa comprovar operação, Agent real, backup, restauração e resposta a falhas.

## Decisão

Usar exclusivamente `development`, a tag `:development`, a stack `trat-dev` e o hostname `trat-hml.outboxtech.com.br`. Não criar stack de produção durante o MVP.

Esta decisão histórica foi substituída em 2026-08-14 pela [ADR-0003](0003-promocao-local-hml-production.md).

## Consequências

- menor complexidade operacional durante aprendizado;
- toda mudança remota impacta o ambiente do piloto e exige disciplina;
- `production` permanece reservada;
- promoção futura depende dos gates documentados de prontidão.
