# MVP de um mês — prontidão para piloto

## Objetivo

Validar durante um mês o ControlPlane central na stack `trat-dev` e Agents instalados em servidores reais ou representativos de clientes.

## Estado em 12/08/2026

| Item | Estado |
| --- | --- |
| Stack `trat-dev` na Contabo | concluído |
| HTTPS em `trat-hml.outboxtech.com.br` | concluído |
| Healthcheck Docker | concluído |
| Login administrativo | concluído |
| Persistência após redeploy | pendente de evidência na VM |
| Backup externo à VPS | pendente |
| Restauração ensaiada | pendente |
| Agent real conectado | pendente |
| Job real para S3 | pendente |
| Produção | não autorizada nesta fase |

## Escopo do piloto

- cadastrar cliente e host;
- instalar e atualizar o Agent;
- receber enroll, heartbeat e configuração;
- executar job manual e agendado;
- confirmar objetos e integridade no S3;
- observar falhas, alertas e retomada de conectividade;
- testar backup e restauração do ControlPlane;
- registrar problemas e decisões do MVP.

## Fora de escopo

- stack de produção;
- alta disponibilidade ou múltiplas réplicas;
- restore de arquivos pelo painel;
- ControlPlane em cada cliente;
- suporte padrão ao Windows Server 2008 sem R2;
- migração para PostgreSQL antes de evidência de necessidade.

## Gates do piloto

Todos devem ser comprovados:

1. `trat-dev` saudável e acessível somente pelo proxy HTTPS;
2. login, sessão e permissões administrativas validados;
3. banco e chaves preservados em redeploy e reboot da VM;
4. backup automático criado e cópia externa configurada;
5. restauração ensaiada com evidência;
6. instalador baixado do painel e Agent instalado;
7. enroll, heartbeat, sync, job manual e job agendado concluídos;
8. objetos confirmados no S3 e segunda execução deduplicada;
9. perda temporária de rede com retomada correta;
10. atualização in-place sem recriar cliente, host ou credenciais;
11. logs sem segredos e runbook de incidentes exercitado;
12. período do piloto concluído sem perda de dados.

## Critério para discutir produção

Produção só entra em planejamento após o aceite registrado dos gates, revisão de segurança, restauração comprovada e definição de responsáveis operacionais. Até lá, todo trabalho permanece em `development` e `trat-dev`.
