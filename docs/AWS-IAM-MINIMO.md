# AWS - IAM minimo para o piloto do TRAT (1 mes)

Este documento define um baseline de permissao para o `TRAT Agent` gravar backups em um bucket S3, com minimo privilegio.

## Premissas

- O Agent precisa:
  - descobrir identidade (STS) para validar AccountId;
  - enviar objetos (PutObject) para um prefixo especifico;
  - opcionalmente listar o bucket/prefixo para checagens e compatibilidade operacional.
- O ControlPlane (painel) pode fazer descoberta de buckets/prefixos via AWS SDK **apenas** se houver credenciais AWS na maquina do painel. Se nao houver, o painel continua funcional para operar hosts/politicas; apenas a descoberta automatica de buckets/prefixos nao funciona.

## Politica recomendada (exemplo)

Substitua:
- `<ACCOUNT_ID>` pela conta AWS do cliente;
- `<BUCKET_NAME>` pelo bucket;
- `<PREFIX>` pelo prefixo (sem barras iniciais), por exemplo `clientes/ep/trat-01/`.

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "TrataIdentity",
      "Effect": "Allow",
      "Action": [
        "sts:GetCallerIdentity"
      ],
      "Resource": "*"
    },
    {
      "Sid": "TrataWriteObjects",
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:AbortMultipartUpload",
        "s3:ListMultipartUploadParts"
      ],
      "Resource": "arn:aws:s3:::<BUCKET_NAME>/<PREFIX>*"
    },
    {
      "Sid": "TrataListPrefixOptional",
      "Effect": "Allow",
      "Action": [
        "s3:ListBucket"
      ],
      "Resource": "arn:aws:s3:::<BUCKET_NAME>",
      "Condition": {
        "StringLike": {
          "s3:prefix": [
            "<PREFIX>*"
          ]
        }
      }
    }
  ]
}
```

## Checklist rapido de validacao

- No host do Agent:
  - `sts:GetCallerIdentity` retorna o `Account` esperado do cliente.
  - Upload de um arquivo pequeno funciona para `s3://<BUCKET_NAME>/<PREFIX>...`.
- No painel:
  - se for usar descoberta de buckets/prefixos, configure credenciais AWS na maquina do painel e valide que o `AccountId` exibido corresponde ao cliente.

## Observacao de seguranca

- Evite credenciais com permissao ampla (ex.: `s3:*`).
- Use prefixo exclusivo por cliente/host para conter impacto em caso de comprometimento do host.
