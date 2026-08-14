# Catálogo de incidentes, bugs e alarmes

Consulte este índice antes de iniciar qualquer correção. Procure pelo sintoma, componente e mensagem de erro. Se o caso ainda não existir, crie um registro antes de alterar código ou infraestrutura.

## Fluxo obrigatório

1. pesquisar neste diretório e em [Resposta a incidentes](../RESPOSTA-A-INCIDENTES.md);
2. registrar data, ambiente, sintoma, impacto e evidências sem segredos;
3. identificar causa raiz ou hipótese verificável;
4. vincular a correção, os testes e o plano de rollback;
5. encerrar somente após validar o artefato realmente distribuído.

## Registros

| ID | Data | Ambiente | Sintoma | Estado |
| --- | --- | --- | --- | --- |
| [INC-2026-08-14-001](INC-2026-08-14-001-agent-apontando-para-ambiente-incorreto.md) | 2026-08-14 | local/hml | Agent reinstalado continua apontando para `localhost` | correção em andamento |
| [INC-2026-08-14-002](INC-2026-08-14-002-instalador-hml-desatualizado.md) | 2026-08-14 | hml | painel distribui instalador anterior à correção | resolvido |
| [INC-2026-08-14-003](INC-2026-08-14-003-controlplane-hml-desatualizado.md) | 2026-08-14 | hml | health responde, mas endpoint de identificação retorna 404 | resolvido |
| [INC-2026-08-14-004](INC-2026-08-14-004-artefato-agent-fora-contexto-docker.md) | 2026-08-14 | hml/production | artefato do Agent excluído do contexto Docker | correção em validação |
| [INC-2026-08-14-005](INC-2026-08-14-005-builds-de-ambientes-compartilhavam-saida.md) | 2026-08-14 | todos | perfis compartilhavam pasta intermediária e bloqueavam publish | correção em validação |
| [INC-2026-08-14-006](INC-2026-08-14-006-checklist-login-falso-positivo.md) | 2026-08-14 | local/hml | checklist podia aceitar retorno à tela de login | correção em validação |
| [ALR-2026-08-14-001](ALR-2026-08-14-001-docker-fora-do-path.md) | 2026-08-14 | local | Docker instalado não é encontrado no `PATH` da sessão | conhecido |
| [ALR-2026-08-14-002](ALR-2026-08-14-002-artefato-local-bloqueado.md) | 2026-08-14 | local | ZIP gerado está bloqueado por outro processo | aberto |
| [INC-2026-08-14-007](INC-2026-08-14-007-variavel-de-ambiente-ausente-no-redeploy.md) | 2026-08-14 | hml | redeploy falha por variável obrigatória ausente e causa 502 | resolvido |

## Modelo mínimo

- ID e título;
- severidade e estado;
- ambientes afetados;
- sintomas e impacto;
- evidências sanitizadas;
- causa raiz;
- correção;
- validação;
- rollback;
- prevenção.
