# MVP 1 Mes - Prontidao

Este documento define quando o MVP esta pronto para um piloto controlado de 1 mes.

## Ja entregue

- backup real do `Agent` para AWS S3 validado;
- configuracao central pelo `ControlPlane`;
- login por usuario e senha no painel;
- administracao de usuarios;
- download do `Agent` pelo painel;
- auditoria administrativa persistida;
- observabilidade operacional por host;
- alertas operacionais persistidos com deduplicacao por causa raiz;
- timeline e relatorios de alertas;
- pacote local do painel para execucao fora do repositorio.

## Fora de escopo por decisao de produto

- restore pela aplicacao;
- publicacao do painel na internet nesta fase.

## Gate minimo para iniciar o piloto

Todos os itens abaixo devem estar `OK`:

1. `OK` pacote local do painel gerado e iniciado em maquina limpa.
2. `OK` login administrativo validado no pacote local.
3. `OK` pagina de download do `Agent` funcionando no pacote local.
4. `OK` fluxo completo `ControlPlane -> Agent -> S3` validado novamente usando o painel local.
5. `OK` backup manual do banco `data\controlplane.db` testado.
6. `OK` restart do servidor:
   - ControlPlane volta automaticamente (via servico Windows ou rotina equivalente);
   - sessao do painel nao quebra por falta de chaves de criptografia (DataProtection persistido).
6. `OK` pelo menos 1 host cliente com:
   - heartbeat regular;
   - sync de configuracao;
   - prechecks saudaveis;
   - 1 job agendado concluido com sucesso.
7. `OK` rotina operacional definida para:
   - reconhecer alertas;
   - atualizar Agent;
   - trocar credencial AWS local;
   - recuperar painel local a partir do backup do SQLite.

## O que ainda precisa acontecer antes de eu cravar "pronto para 1 mes"

- validar o novo pacote local do painel em execucao real por copia de arquivos;
- testar a pagina de downloads do `Agent` a partir desse pacote local;
- executar um ciclo final de smoke test com pelo menos 1 host real apontando para essa instancia local;
- validar o backup do banco local e a reabertura do painel com a mesma base.
- validar execucao do painel como servico Windows em servidor de teste e reboot controlado.

## Criterio de decisao

Quando os quatro itens acima forem validados sem regressao, minha recomendacao muda para:

- `Pronto para piloto controlado de 1 mes`

Antes disso, minha recomendacao permanece:

- `Muito proximo, mas ainda precisa da validacao final do pacote local`
