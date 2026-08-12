# Segurança e gestão de segredos

## Classificação

| Classe | Exemplos | Tratamento |
| --- | --- | --- |
| Público | documentação, hostname, imagem pública | pode ser versionado |
| Interno | arquitetura detalhada, logs sanitizados | acesso operacional |
| Confidencial | e-mails, dados de clientes, manifests | mínimo necessário |
| Segredo | senhas, tokens, chaves AWS, cookies | nunca versionar ou compartilhar em texto aberto |

## Onde segredos podem existir

- variáveis da stack no Portainer;
- `.env` local;
- DPAPI no host do Agent;
- credenciais AWS;
- tokens de enrollment;
- sessões e chaves Data Protection;
- backups do banco.

## Regras

- nunca registrar segredos no Git, documentação, issue ou screenshot;
- usar senha diferente por ambiente;
- usar credencial AWS de mínimo privilégio e prefixo restrito;
- não reutilizar token de cliente;
- rotacionar após exposição ou mudança de responsável;
- sanitizar logs antes de compartilhar;
- manter backups criptografados e com acesso auditável;
- não habilitar API administrativa sem necessidade aprovada.

## Bootstrap administrativo

`TRAT_ADMIN_PASSWORD` serve apenas para criar o primeiro administrador. Alterar a variável não troca a senha de usuário já existente. Após validar o primeiro login, mantenha um valor descartável forte se o Compose exigir a variável e gerencie a conta pelo painel.

## Repositório e pacote públicos

Código e imagem são públicos. Isso exige que:

- nenhum segredo seja incorporado no Dockerfile ou na imagem;
- valores reais não existam em exemplos;
- artefatos do Agent sejam revisados para não conter credenciais;
- logs do GitHub Actions não exibam parâmetros sensíveis.

## Resposta a exposição

Revogue primeiro, investigue depois. Preserve evidência sem preservar o segredo em canais inseguros. Siga [RESPOSTA-A-INCIDENTES.md](RESPOSTA-A-INCIDENTES.md).
