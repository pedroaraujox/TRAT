# Operação do MVP

## Inventário

```text
Ambiente: desenvolvimento/MVP
Stack: trat-dev
Container principal: trat-dev
URL: https://trat-hml.outboxtech.com.br
Health: https://trat-hml.outboxtech.com.br/api/v1/health
Branch: development
Imagem: ghcr.io/pedroaraujox/trat-controlplane:development
Proxy: Nginx Proxy Manager
Banco: SQLite
```

## Verificação diária

1. conferir se `trat-dev` está `running` e `healthy`;
2. abrir o health público e esperar `{"status":"ok"}`;
3. conferir dashboard, hosts sem heartbeat, jobs falhos e alertas;
4. revisar logs recentes do container;
5. verificar existência de backup recente;
6. registrar qualquer anomalia.

## Atualização normal

1. confirmar workflow verde para o commit desejado;
2. anotar SHA e horário;
3. criar ou confirmar backup recente;
4. clicar **Pull and redeploy** no Portainer;
5. não remover volumes;
6. aguardar `healthy`;
7. testar health, login e fluxo alterado;
8. observar logs e Agents por pelo menos 15 minutos em mudança relevante.

## Reboot da VM

Depois de reiniciar a Contabo:

1. confirmar Docker e Portainer;
2. confirmar Nginx Proxy Manager;
3. confirmar `trat-dev` saudável;
4. testar HTTPS e login;
5. validar que clientes e usuários continuam presentes;
6. confirmar heartbeat dos Agents.

## Logs

No Portainer, abra **Containers > trat-dev > Logs**. Não copie logs completos para canais públicos. Remova tokens, chaves, e-mails e caminhos sensíveis antes de compartilhar trechos.

## Mudanças proibidas no fluxo comum

- remover volumes;
- escalar para mais de uma réplica;
- publicar a porta 8080;
- executar comandos de exclusão no volume SQLite;
- editar o banco manualmente sem backup;
- criar stack de produção;
- usar `latest` como tag;
- habilitar a API administrativa sem revisão.

## Escalonamento

Falhas seguem [RESPOSTA-A-INCIDENTES.md](RESPOSTA-A-INCIDENTES.md). Perda ou corrupção de dados segue [BACKUP-E-RESTAURACAO.md](BACKUP-E-RESTAURACAO.md).
