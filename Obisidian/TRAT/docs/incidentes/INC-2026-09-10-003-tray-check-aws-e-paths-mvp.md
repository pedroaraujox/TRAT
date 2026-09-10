# INC-2026-09-10-003 - Tray invisivel, descoberta AWS desatualizada e paths bloqueados

- Data: 2026-09-10
- Ambiente: HML / Agent Windows
- Severidade: alta para homologacao do MVP
- Estado: correcao local; validacao real pendente

## Sintomas

1. o instalador conclui, mas o Tray nao aparece e um segundo clique no executavel nao abre a janela;
2. alteracoes nos buckets AWS nao podem ser atualizadas sob demanda pelo painel;
3. ao salvar a politica do host, o painel informa que o Agent ainda nao reportou paths iniciais.

## Causas

- a segunda instancia do Tray encerrava silenciosamente por causa do mutex, sem sinalizar a instancia existente para abrir a janela;
- a descoberta AWS era publicada periodicamente pelo Agent, mas nao havia um comando remoto explicito no ControlPlane;
- a tela e a mensagem ainda refletiam o fluxo antigo em que os paths nasciam no Agent, contrariando o onboarding minimo atual.

## Correcao

- o Tray abre a janela de status ao iniciar e uma segunda execucao sinaliza a instancia existente;
- o instalador verifica se o Tray permanece em execucao e o script confirma o servico em `Running`;
- o botao `Checar AWS` enfileira `aws_check`; o Agent atualiza conta, buckets e regioes e conclui a solicitacao sem criar job de backup;
- a descoberta automatica continua ocorrendo pelo relatorio periodico do Agent;
- paths digitados no ControlPlane sao aceitos independentemente do antigo indicador de bootstrap.

## Validacao

- build Release sem warnings: concluido;
- testes automatizados do ControlPlane: 16/16;
- pacote versionado e teste no servidor real: pendentes.

## Rollback

Reinstalar o pacote anterior e reimplantar a imagem anterior do ControlPlane. A correcao nao remove configuracoes, volumes nem objetos S3.
