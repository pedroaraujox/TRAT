# Debug Session: agent-metadata-ignore

- Status: OPEN
- StartedAt: 2026-06-22
- Symptom: mesmo após a implementação para ignorar `desktop.ini`, um novo backup ainda enviou esse arquivo para o S3.
- Scope: scanner do Agent, pacote distribuído pelo painel, versão instalada no host e evidência de runtime do job.

## Hypotheses

1. O host ainda está executando uma versão antiga do Agent, sem a exclusão automática de `desktop.ini`.
2. O pacote mais novo foi gerado no repositório, mas o painel/host ainda não consumiu esse pacote atualizado.
3. O arquivo `desktop.ini` não está passando pelo `FileScanner` esperado e entra por outro caminho no pipeline do manifest/upload.
4. A exclusão foi implementada corretamente, mas o job visualizado no S3 é de uma execução anterior ao update do Agent.
5. A comparação do nome do arquivo falha em algum cenário de path/nome retornado pelo runtime no host.

## Evidence Log

- Pending collection.

## Next Steps

1. Confirmar o conteúdo atual do scanner no código e a presença da correção no pacote publicado.
2. Verificar qual versão/binário está instalado no host.
3. Ler logs/manifests do Agent no host para o job que ainda enviou `desktop.ini`.
4. Só então decidir se o problema é versão desatualizada, empacotamento ou falha real da exclusão.
