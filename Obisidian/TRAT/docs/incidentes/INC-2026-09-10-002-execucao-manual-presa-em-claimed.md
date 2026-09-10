# INC-2026-09-10-002 — Execução manual presa em CLAIMED

- Data: 2026-09-10
- Ambiente: Agent e ControlPlane
- Severidade: SEV-3
- Estado: correção implementada localmente; publicação e validação real pendentes

## Sintoma e impacto

Uma solicitação manual era consumida pelo Agent e ficava em `CLAIMED` quando a política não possuía destino AWS ou paths completos. O Agent encerrava o método antes de criar o job e enviar o resultado final, deixando o painel sem diagnóstico conclusivo.

## Causa raiz

As validações de configuração executadas antes da criação do job usavam retorno antecipado sem correlacionar uma falha final ao `RunRequestId` já consumido.

## Correção

O Agent agora cria e finaliza um job como `FAILED` quando a política recebida estiver incompleta. Os códigos são:

- `AWS_POLICY_INCOMPLETE` para região, bucket ou prefixo ausentes;
- `BACKUP_PATHS_MISSING` quando nenhuma pasta de backup foi definida.

O resultado final mantém a correlação com a solicitação manual e usa a mesma outbox resiliente dos demais relatórios finais.

## Validação necessária

1. solicitar execução manual sem política completa;
2. confirmar transição de `CLAIMED` para `FAILED`;
3. conferir código e mensagem acionáveis no painel;
4. completar a política e repetir até `SUCCEEDED`.

## Rollback

Reinstalar o pacote anterior do Agent. Nenhum dado, credencial ou objeto S3 é removido pela correção.
