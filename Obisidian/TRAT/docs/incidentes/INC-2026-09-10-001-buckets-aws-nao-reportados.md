# INC-2026-09-10-001 — Buckets AWS não reportados pelo Agent

- Data: 2026-09-10
- Ambiente: homologação
- Severidade: SEV-3
- Estado: correção implementada localmente; publicação e validação real pendentes

## Sintoma e impacto

O host `WIN-FIPMGQJGI6D` mantém heartbeat e serviço do Agent em execução, mas o seletor **Bucket S3** contém apenas `Selecione um bucket`. Sem um bucket selecionável, a política inicial e o primeiro job real para S3 não podem ser concluídos pelo painel.

## Evidências sanitizadas

- homologação executando a revisão `46ff4fb323b582088c3e2495ba41a21afe1a8102`;
- último precheck exibido no painel: credencial AWS validada por STS e conta identificada;
- `PrecheckCredentialOk` aparece como verdadeiro;
- integração AWS da configuração exibe conta reportada como não identificada e nenhum bucket;
- lista HTML de buckets contém somente a opção vazia;
- o formulário referencia uma configuração válida e a conta AWS esperada do cliente.

Nenhuma chave, segredo ou token foi registrado.

## Diagnóstico

O fluxo atual permite que `ListBucketsAsync` falhe e continue com `CredentialOk = true`. A exceção é gravada somente no log local do Agent; o relatório enviado ao ControlPlane recebe uma lista vazia e não contém a causa da falha. Assim, a interface não distingue entre:

1. credencial sem `s3:ListAllMyBuckets`;
2. Agent distribuído incompatível ou desatualizado;
3. falha transitória da AWS;
4. perda dos campos de descoberta durante envio ou persistência.

A evidência disponível torna a ausência ou negação de `s3:ListAllMyBuckets` a hipótese principal, mas ela ainda precisa ser confirmada no log local do Agent ou por teste IAM controlado. O campo de conta ausente no registro, apesar de o texto do precheck conter a conta, indica também uma inconsistência independente no payload/persistência que deve ser rastreada.

## Correção recomendada

1. propagar no relatório um status e uma mensagem sanitizada da descoberta de buckets;
2. não apresentar credencial AWS como integralmente pronta quando a descoberta obrigatória falhar;
3. registrar telemetria do ControlPlane com presença e quantidade dos campos, sem nomes de buckets ou segredos;
4. identificar a revisão real do Agent no relatório, além da versão fixa `1.0.0.0`;
5. confirmar no IAM a ação `s3:ListAllMyBuckets` com `Resource: *`;
6. após corrigir a causa, aguardar novo sincronismo e verificar conta, buckets e regiões no painel.

## Correção implementada

- o Agent passou a reportar separadamente sucesso/falha e diagnóstico sanitizado da descoberta;
- uma negação da AWS orienta explicitamente a conferir `s3:ListAllMyBuckets`;
- o ControlPlane persiste o resultado sem quebrar compatibilidade com Agents anteriores;
- a interface diferencia pacote antigo, listagem negada e conta sem buckets visíveis;
- persistência coberta por teste automatizado;
- ControlPlane e Agent compilados localmente sem erros.

## Validação necessária

- o novo relatório persiste a conta AWS identificada por STS;
- o seletor mostra todos os buckets visíveis à credencial;
- a região é atualizada ao selecionar cada bucket;
- uma negação IAM aparece como diagnóstico acionável, sem expor dados sensíveis;
- o artefato validado é o mesmo efetivamente instalado no host.

## Rollback

Alterações de telemetria devem ser compatíveis com Agents anteriores por campos opcionais. Se houver regressão, restaurar o Agent anterior e manter a política sem bucket até nova validação; não alterar nem remover credenciais, objetos ou volumes.
