# Backup e restauração

## Escopo

O backup operacional deve proteger:

- `controlplane.db`;
- chaves ASP.NET Data Protection em `data/keys`;
- configuração da stack e valores necessários para reconstrução;
- evidência da versão da imagem em execução.

## Backup automático atual

O ControlPlane usa a API de backup do SQLite para gerar uma cópia consistente:

```text
/app/backups/controlplane-AAAAMMDD-HHMMSS.db
```

Na VM, o intervalo é 24 horas, atraso inicial de 5 minutos e retenção local de 30 dias.

## Limitação crítica

O volume `backups` está na mesma VPS. Ele protege contra erro na aplicação, mas não contra perda total da VM ou da conta Contabo. É obrigatório criar cópia externa periódica para armazenamento criptografado e com retenção própria.

## Política mínima do MVP

- backup consistente diário;
- cópia externa diária ou, no máximo, a cada 24 horas;
- retenção externa de 30 dias;
- teste de restauração mensal e antes de mudança de schema relevante;
- acesso restrito aos responsáveis operacionais;
- registro de data, tamanho, checksum e destino.

## Antes de uma mudança de risco

1. confirme backup recente;
2. copie o backup para fora da VPS;
3. registre SHA da imagem atual;
4. confirme que o arquivo copiado possui tamanho maior que zero;
5. calcule checksum quando possível.

## Procedimento de restauração

A restauração é uma operação de impacto e deve ocorrer em janela controlada.

1. declare incidente ou manutenção;
2. bloqueie mudanças administrativas;
3. pare somente o container `trat-dev`;
4. preserve uma cópia do banco atual, mesmo suspeito;
5. escolha backup consistente e valide tamanho/checksum;
6. substitua o banco no volume `data` mantendo propriedade do usuário `app`;
7. preserve/restaure também `data/keys` quando reconstruir o volume;
8. inicie o container;
9. aguarde `healthy`;
10. valide login, clientes, hosts, jobs e heartbeat;
11. registre perda temporal estimada e evidências.

Não execute restauração enquanto o ControlPlane escreve no banco.

## Teste de restauração

O ensaio deve ser feito em ambiente isolado, nunca sobre a única stack ativa sem necessidade. Critérios:

- banco abre sem erro;
- login funciona;
- dados esperados existem;
- chaves de sessão são coerentes;
- aplicação fica saudável;
- nenhum Agent aponta acidentalmente para o ambiente de teste.

## RPO e RTO iniciais

- RPO alvo do MVP: até 24 horas;
- RTO alvo do MVP: até 4 horas.

Esses valores são objetivos iniciais e devem ser revisados após o piloto.
