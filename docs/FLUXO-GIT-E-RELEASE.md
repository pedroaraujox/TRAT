# Fluxo Git e release

## Branches

| Branch | Uso atual |
| --- | --- |
| `development` | fonte de verdade do MVP e da stack `trat-dev` |
| `production` | reservada; sem stack ativa |
| `main` | histórico/base do repositório; não publica o MVP |

Toda implementação do MVP deve terminar em `development`. Não faça merge em `production` durante esta fase.

## Commits

- pequenos e com escopo claro;
- sem segredos ou artefatos gerados;
- documentação no mesmo commit quando o comportamento mudar;
- mensagem no formato `tipo: descrição`, por exemplo `fix: correct container healthcheck`.

## Pipeline

Push em `development`:

1. gera o Agent Windows apontando para o ambiente MVP;
2. incorpora o Setup na imagem;
3. constrói a imagem Linux;
4. publica `:development` e `:<SHA>` no GHCR.

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

## Produção futura

A futura promoção exigirá pull request `development -> production`, revisão, gates do piloto, backup comprovado e plano de rollback. A existência da branch ou tag não autoriza criar a stack.
