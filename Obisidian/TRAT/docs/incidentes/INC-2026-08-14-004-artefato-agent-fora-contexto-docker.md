# INC-2026-08-14-004 — Artefato do Agent fora do contexto Docker

- Severidade: SEV-2
- Estado: corrigido no código, aguardando validação distribuída
- Ambientes: homologação e produção

## Sintoma e impacto

O GitHub Actions gera o Agent em `artifacts/agent-package-release`, mas o `.dockerignore` não liberava esse diretório para o contexto de build. A imagem poderia falhar ao construir ou não incorporar o instalador recém-gerado.

## Causa raiz

O nome do diretório usado pelo workflow divergiu das exceções mantidas no `.dockerignore`.

## Correção

Liberar explicitamente `agent-package-release` e os três diretórios de ambiente somente no contexto Docker. O `.gitignore` continua impedindo versionamento dos binários.

## Validação

O pipeline deve construir a imagem, e o instalador baixado do ControlPlane deve conter manifesto com ambiente, URL e data correspondentes à execução atual.
