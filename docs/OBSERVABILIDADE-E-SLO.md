# Observabilidade e objetivos de serviço

## Sinais atuais

- health HTTP em `/api/v1/health`;
- healthcheck Docker;
- logs do ControlPlane no Portainer;
- heartbeat e versão dos Agents;
- estado dos jobs e alertas;
- arquivos de backup gerados;
- disponibilidade do Nginx Proxy Manager.

## SLO inicial do MVP

| Indicador | Objetivo inicial |
| --- | --- |
| Disponibilidade mensal do painel | 99% |
| Detecção de indisponibilidade | até 15 minutos |
| RPO do ControlPlane | até 24 horas |
| RTO do ControlPlane | até 4 horas |
| Host sem heartbeat | investigar após 10 minutos |
| Job atrasado | investigar após 90 minutos |

Os objetivos são internos e devem ser recalibrados com dados do piloto.

## Verificação operacional

- disponibilidade não é apenas HTTP 200: login e leitura do banco também devem funcionar;
- container `unhealthy` deve ser tratado mesmo que o proxy responda;
- backup só é válido após teste de restauração;
- job só é sucesso quando contagem e bytes planejados correspondem aos confirmados no S3.

## Logs

Os logs devem conter contexto técnico, timestamp UTC e identificadores operacionais, mas não senhas, tokens, chaves AWS ou conteúdo de arquivos de clientes.

## Evolução recomendada

Após estabilizar o MVP:

- monitor externo do health HTTPS;
- alerta de container unhealthy;
- alerta de backup atrasado;
- métricas de jobs e heartbeat;
- centralização de logs com retenção definida;
- dashboard de SLO e incidentes.
