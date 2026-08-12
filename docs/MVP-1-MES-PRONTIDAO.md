# MVP de homologacao central - prontidao para piloto

## Objetivo

Validar por um mes um ControlPlane central operado pela Outbox Tech e Agents instalados em servidores de clientes.

## Entregue no codigo

- backup real do Agent para AWS S3;
- configuracao e monitoramento pelo ControlPlane;
- painel com login, usuarios, auditoria e bloqueio de tentativas;
- pacote central autocontido;
- Setup do Agent apontando para a URL HTTPS de homologacao;
- tokens de Agent separados por cliente;
- alertas e observabilidade por host;
- hardening HTTP e API administrativa desativada por padrao;
- scripts de Tunnel, backup e validacao publica.

## Fora de escopo

- restore de arquivos pela aplicacao;
- alta disponibilidade do ControlPlane;
- painel executado em cada servidor cliente;
- suporte padrao ao Windows Server 2008 sem R2.

## Gate para iniciar o piloto

Todos os itens devem estar `OK`:

1. servidor central Windows Server 2022 x64 atualizado;
2. `trat-hml.outboxtech.com.br` publicado pelo Nginx Proxy Manager da Contabo, sem porta 8080 exposta no host;
3. HTTPS, health, login e cabecalhos validados pelo script de homologacao;
4. backup diario executado e restauracao ensaiada;
5. reinicio do servidor com retorno automatico de ControlPlane e Tunnel;
6. download real do Setup pelo painel;
7. Agent instalado em pelo menos um Windows Server 2012 R2 e um Windows Server moderno;
8. enroll, heartbeat, sync, job manual e job agendado concluidos;
9. arquivos confirmados no S3 e segunda execucao deduplicada;
10. perda temporaria de internet com retomada e envio do status final pendente;
11. atualizacao in-place sem recriar cliente, host ou credenciais;
12. logs sem segredos e rotina de resposta a alertas definida.

## Estado

O codigo e o pacote podem ser considerados prontos somente depois da validacao no servidor externo. Sem servidor, rota DNS e token de Tunnel, a publicacao permanece preparada, mas nao comprovada.
