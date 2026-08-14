# AWS — IAM mínimo para o Agent

## Princípio

O Agent pode gravar e verificar backups, mas nunca excluir objetos. Cada cliente ou host deve usar credencial e prefixo próprios sempre que possível.

## Política base

Substitua `<BUCKET_NAME>` e `<PREFIX>`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "TratIdentity",
      "Effect": "Allow",
      "Action": "sts:GetCallerIdentity",
      "Resource": "*"
    },
    {
      "Sid": "TratWriteObjects",
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:AbortMultipartUpload",
        "s3:ListMultipartUploadParts"
      ],
      "Resource": "arn:aws:s3:::<BUCKET_NAME>/<PREFIX>*"
    },
    {
      "Sid": "TratDiscoverBuckets",
      "Effect": "Allow",
      "Action": "s3:ListAllMyBuckets",
      "Resource": "*"
    },
    {
      "Sid": "TratReadMetadata",
      "Effect": "Allow",
      "Action": [
        "s3:ListBucket",
        "s3:GetBucketLocation"
      ],
      "Resource": "arn:aws:s3:::<BUCKET_NAME>",
      "Condition": {
        "StringLike": {
          "s3:prefix": ["<PREFIX>*"]
        }
      }
    }
  ]
}
```

`HeadObject` é autorizado por `s3:GetObject` no modelo de permissões do S3. Se a verificação do Agent exigir essa chamada, conceda `s3:GetObject` apenas ao mesmo prefixo, documentando a decisão.

## Controles obrigatórios do bucket

- Block Public Access;
- versionamento;
- criptografia em repouso;
- Object Lock quando criado com esse recurso;
- política explícita de negação de exclusão;
- acesso administrativo separado da credencial do Agent.

## Proibições

Não conceda ao Agent:

- `s3:DeleteObject`;
- `s3:DeleteObjectVersion`;
- `s3:PutBucketPolicy`;
- `s3:PutLifecycleConfiguration`;
- `s3:*`.

## Validação

1. confirmar `sts:GetCallerIdentity`;
2. confirmar que `s3:ListAllMyBuckets` lista os buckets que devem aparecer no painel;
3. enviar arquivo pequeno ao prefixo correto;
4. confirmar leitura de metadados necessária;
5. comprovar que uma tentativa de exclusão é negada;
6. conferir que logs não exibem chaves AWS.

Credenciais devem seguir [SEGURANCA-E-SEGREDOS.md](SEGURANCA-E-SEGREDOS.md).
