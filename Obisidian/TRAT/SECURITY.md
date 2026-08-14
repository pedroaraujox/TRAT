# Política de segurança

## Reporte responsável

Não publique vulnerabilidades, tokens, credenciais, dados de clientes ou detalhes exploráveis em issues públicas.

Use um canal privado administrado pela Outbox Tech ou o recurso **Report a vulnerability** do GitHub, quando habilitado no repositório. Inclua descrição, impacto, versão/commit afetado e passos mínimos de reprodução sem dados reais.

## Escopo prioritário

- autenticação e autorização;
- isolamento entre clientes;
- exposição de tokens e credenciais AWS;
- upload indevido ou exclusão no S3;
- acesso ao SQLite e backups;
- execução remota no Agent ou ControlPlane;
- bypass do proxy/HTTPS;
- supply chain de imagem e instalador.

## Segredos encontrados no Git

Considere o segredo comprometido mesmo após apagar o arquivo. Revogue-o imediatamente, investigue o histórico e siga o runbook de incidentes.

## Versões suportadas

Correções são desenvolvidas localmente, publicadas em `development` e validadas em homologação. Produção recebe apenas o mesmo commit aprovado, por merge revisado, com credenciais e dados isolados.
