# Fluxo Git e release

## Branches

| Branch | Uso atual |
| --- | --- |
| `development` | integração contínua e fonte da homologação `trat-dev` |
| `production` | versão aprovada para a stack `trat-prod` |
| `main` | histórico/base do repositório; não publica o MVP |

Toda implementação começa localmente e segue para `development`. O merge `development -> production` só ocorre depois da homologação completa do mesmo commit e de uma decisão explícita de liberação.

## Commits

- pequenos e com escopo claro;
- sem segredos ou artefatos gerados;
- documentação no mesmo commit quando o comportamento mudar;
- mensagem no formato `tipo: descrição`, por exemplo `fix: correct container healthcheck`.

## Pipeline

Push em `development`:

1. executa testes e build em Release;
2. gera o Agent Windows apontando para homologação;
3. incorpora o Setup na imagem;
4. constrói a imagem Linux;
5. publica `:development` e `:<SHA>` no GHCR.

Push em `production` após merge aprovado:

1. repete testes e build em Release;
2. gera o Agent apontando para produção;
3. publica `:production` e `:<SHA>`;
4. permite o redeploy controlado de `trat-prod`.

A tag por SHA é a referência de auditoria e rollback. A tag `:development` é móvel.

## Deploy

O pipeline publica, mas não altera automaticamente a VM. O operador confirma o workflow e executa **Pull and redeploy** no Portainer.

## Versionamento

Enquanto não houver releases semânticos, registre cada implantação pelo SHA Git. Antes de distribuição comercial, adote SemVer e tags assinadas.

## Rollback

- prefira retornar à imagem `:<SHA>` aprovada;
- faça backup antes de rollback com alteração de schema;
- valide compatibilidade do banco;
- preserve volumes;
- registre motivo, período e resultado.

## Gate de produção

A promoção exige pull request `development -> production`, revisão, checklist de homologação assinado, backup/restauração comprovados, imagem por SHA conhecida e plano de rollback. Nunca promova um commit diferente daquele validado em homologação.
