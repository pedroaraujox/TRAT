# INC-2026-09-10-004 — Backup para bucket S3 excluído

- Data: 2026-09-10
- Ambiente: homologação e Agent Windows
- Severidade: SEV-2
- Estado: correção implementada localmente; publicação e validação real pendentes

## Sintoma e impacto

Um job real terminou em falha com 26 ocorrências `AMAZONS3EXCEPTION`, uma para cada arquivo planejado. Nenhum arquivo foi confirmado no S3. A mensagem resumida mostrava principalmente os paths locais e não deixava evidente que o destino configurado havia sido excluído.

## Evidências sanitizadas

- Agent instalado: `1.0.0+84172e0b15fb`;
- ControlPlane HML: revisão `84172e0b15fbe00a592cfff7da78cccd4348588c`;
- STS validou a credencial e a conta retornou 22 buckets visíveis;
- as 26 falhas de upload continham a resposta AWS `The specified bucket does not exist`;
- o usuário confirmou que excluiu o bucket anterior e criou outro.

Nenhuma chave, segredo, token ou nome de bucket foi registrado neste incidente.

## Causa raiz

A política efetiva ainda apontava para o bucket excluído. O job verificava a existência dos objetos individualmente e tratava uma resposta S3 de bucket inexistente como se fosse ausência do objeto. Em seguida, cada upload falhava de forma independente, sem um precheck bloqueante do destino antes da varredura e do envio.

## Correção

- executar o readiness AWS completo para região, bucket e prefixo antes de iniciar um job real;
- encerrar o job imediatamente com `AWS_DESTINATION_UNAVAILABLE` quando o bucket não existir, estiver inacessível ou estiver em região incompatível;
- incluir a mensagem sanitizada da causa nas amostras apresentadas no resumo de problemas.

## Validação necessária

1. publicar o novo Agent no pacote de HML e instalá-lo no host de teste;
2. selecionar no ControlPlane um bucket existente retornado pelo botão **Checar AWS** e salvar a política;
3. executar um backup pequeno e confirmar estado `SUCCEEDED`, bytes e itens equivalentes ao manifest;
4. selecionar ou simular um bucket removido e confirmar falha única `AWS_DESTINATION_UNAVAILABLE`, sem tentativa arquivo a arquivo;
5. confirmar no bucket os objetos e metadados de integridade esperados.

## Rollback

Reinstalar o pacote anterior do Agent. O rollback não altera credenciais, políticas, objetos S3 nem volumes do ControlPlane. Até nova correção, não executar jobs quando o bucket selecionado tiver sido removido.

## Prevenção

Todo job real deve validar o destino remoto antes de enumerar e enviar arquivos. Mudanças externas na AWS devem ser reconciliadas pelo **Checar AWS** e pela checagem periódica, mas o precheck do job continua obrigatório por causa da janela entre descoberta e execução.
