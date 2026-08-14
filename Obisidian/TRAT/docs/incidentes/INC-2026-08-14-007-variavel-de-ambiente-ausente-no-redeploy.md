# INC-2026-08-14-007 — Variável obrigatória ausente no redeploy do Portainer

- Severidade: SEV-1
- Estado: resolvido
- Ambiente: homologação

## Sintoma e impacto

Durante o redeploy de `trat-dev`, o container anterior foi removido e o novo compose foi rejeitado. O proxy público retornou HTTP 502 e o stack ficou temporariamente sem containers.

Mensagem sanitizada: `required variable TRAT_ENVIRONMENT_NAME is missing a value`.

## Causa raiz

O compose passou a exigir `TRAT_ENVIRONMENT_NAME`, mas essa variável ainda não existia na configuração persistida do stack Git do Portainer. A validação local e o CI forneciam a variável, portanto não reproduziam a configuração legada do stack remoto.

## Correção aplicada

- adicionada `TRAT_ENVIRONMENT_NAME=hml` ao stack `trat-dev`;
- configuração salva antes de um novo redeploy;
- imagem `:development` repuxada novamente;
- volumes existentes foram preservados.

## Validação

- stack recriado e endpoint público saudável;
- `/api/v1/environment` identificou `hml` e a revisão `87fc9106694a6fb2960c39e7238348244ba8478c`;
- login, dados persistidos e download do Agent validados.

## Rollback

Reimplantar uma tag SHA conhecida, mantendo `TRAT_ENVIRONMENT_NAME=hml` e sem remover volumes.

## Prevenção

- antes de redeploy, comparar todas as variáveis obrigatórias do compose com as variáveis do stack remoto;
- adicionar novas variáveis com valor compatível no Portainer antes de tornar a interpolação obrigatória;
- testar `/api/v1/environment` além do healthcheck;
- nunca usar remoção de volumes durante rollback ou atualização rotineira.
