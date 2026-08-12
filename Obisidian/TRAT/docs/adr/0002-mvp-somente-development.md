# ADR-0002 — MVP somente no ambiente development

- Data: 2026-08-12
- Estado: aceita

## Contexto

O TRAT entrou na fase de piloto e ainda precisa comprovar operação, Agent real, backup, restauração e resposta a falhas.

## Decisão

Usar exclusivamente `development`, a tag `:development`, a stack `trat-dev` e o hostname `trat-hml.outboxtech.com.br`. Não criar stack de produção durante o MVP.

## Consequências

- menor complexidade operacional durante aprendizado;
- toda mudança remota impacta o ambiente do piloto e exige disciplina;
- `production` permanece reservada;
- promoção futura depende dos gates documentados de prontidão.
