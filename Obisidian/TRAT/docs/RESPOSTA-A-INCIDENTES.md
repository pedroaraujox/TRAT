# Resposta a incidentes

## Severidade

| Nível | Exemplo | Resposta inicial |
| --- | --- | --- |
| SEV-1 | perda/corrupção de dados, credencial exposta, indisponibilidade total | imediata |
| SEV-2 | Agents sem comunicar, jobs falhando em massa, backup atrasado | até 1 hora |
| SEV-3 | falha isolada, degradação sem perda de dados | próximo período operacional |

## Primeiros passos

1. pesquisar o sintoma no [catálogo de incidentes](incidentes/README.md);
2. criar ou atualizar o registro do incidente antes da correção;
3. registrar horário, impacto e primeiro sintoma;
4. evitar redeploys repetidos e ações destrutivas;
5. preservar logs, versão da imagem e estado dos containers;
6. verificar health público e interno;
7. verificar Nginx, DNS, stack, volumes e espaço em disco;
8. identificar última mudança;
9. comunicar status sem expor segredos.

## Serviço indisponível

Verifique nesta ordem:

1. DNS resolve para a Contabo;
2. Nginx Proxy Manager está ativo;
3. `trat-dev` está running;
4. healthcheck mostra o erro real;
5. rede `nginx-proxy_default` contém proxy e `trat-dev`;
6. volumes estão montados;
7. logs indicam banco, permissão ou configuração inválida.

## Container unhealthy com site acessível

Inspecione `State.Health.Log`. Confirme hostname do healthcheck, porta 8080 e resposta do endpoint. Não desative o healthcheck como solução.

## Suspeita de credencial exposta

1. trate como SEV-1;
2. revogue/rotacione token ou chave;
3. preserve evidências sem republicar o segredo;
4. verifique histórico Git, Actions, Portainer e logs;
5. regenere token do cliente quando aplicável;
6. avalie objetos S3 e eventos CloudTrail;
7. documente causa e prevenção.

## Suspeita de corrupção do SQLite

1. pare o container;
2. preserve banco, WAL e SHM atuais;
3. não execute ferramentas de reparo sobre a única cópia;
4. selecione backup consistente;
5. siga o runbook de restauração;
6. registre dados potencialmente perdidos desde o backup.

## Encerramento

Um incidente só termina após:

- serviço estabilizado;
- dados validados;
- causa raiz ou hipótese mais provável registrada;
- credenciais rotacionadas quando necessário;
- ação preventiva definida;
- documentação corrigida.

Use o modelo [MODELO-POSTMORTEM.md](MODELO-POSTMORTEM.md).
