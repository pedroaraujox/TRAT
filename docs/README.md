# Documentação do TRAT

## Comece aqui

| Necessidade | Documento |
| --- | --- |
| Entender o sistema | [Arquitetura central](ARQUITETURA-CENTRAL-E-HOMOLOGACAO.md) |
| Rodar localmente | [Como rodar localmente](COMO-RODAR-LOCALMENTE.md) |
| Operar a stack atual | [Operação do MVP](OPERACAO-MVP.md) |
| Publicar na Contabo | [Portainer](../deploy/portainer/README.md) |
| Atualizar com segurança | [Fluxo Git e release](FLUXO-GIT-E-RELEASE.md) |
| Proteger segredos | [Segurança e segredos](SEGURANCA-E-SEGREDOS.md) |
| Recuperar dados | [Backup e restauração](BACKUP-E-RESTAURACAO.md) |
| Tratar falhas | [Resposta a incidentes](RESPOSTA-A-INCIDENTES.md) |
| Monitorar o serviço | [Observabilidade e SLO](OBSERVABILIDADE-E-SLO.md) |
| Preparar o piloto | [Prontidão do MVP](MVP-1-MES-PRONTIDAO.md) |
| Instalar Agent | [Manual do Agent](../deploy/agent/README.md) |
| Configurar AWS | [IAM mínimo](AWS-IAM-MINIMO.md) |

## Governança

- [Contribuição](../CONTRIBUTING.md)
- [Política de segurança](../SECURITY.md)
- [Changelog](../CHANGELOG.md)
- [Checklist de desenvolvimento](CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md)
- [Decisões arquiteturais](adr/README.md)

## Fonte de verdade

O estado atual é:

- `development` e `trat-dev` são o único fluxo remoto ativo;
- `production` está reservada e não implantada;
- Docker/Portainer/Nginx Proxy Manager são a arquitetura principal;
- Windows/Cloudflare não é a arquitetura da Contabo;
- SQLite permanece durante o piloto, com uma única réplica.

Se um documento divergir desses pontos, deve ser corrigido antes de orientar uma operação.

## Manutenção documental

Toda mudança que altere comportamento, configuração, deploy, segurança ou recuperação deve atualizar o documento correspondente no mesmo commit. Use datas absolutas para fatos operacionais e não registre segredos.
