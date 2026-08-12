# ControlPlane — pacote Windows alternativo

O ControlPlane oficial do MVP roda em Docker na Contabo. Esta pasta mantém o pacote Windows para desenvolvimento, diagnóstico e contingência planejada; ela não é o procedimento padrão da VM.

## Uso local

1. copie o pacote para uma pasta local não sincronizada;
2. execute `Iniciar-Painel-Local.cmd`;
3. informe a conta administrativa inicial;
4. acesse `http://localhost:5080`.

## Arquivos

| Arquivo | Finalidade |
| --- | --- |
| `Iniciar-Painel-Local.cmd` | inicia o painel local |
| `Parar-Painel-Local.ps1` | encerra o processo local |
| `Primeira-Configuracao.ps1` | cria configuração inicial |
| `Backup-Painel-Dados.ps1` | backup manual do SQLite Windows |
| `Restaurar-Painel-Dados.ps1` | restauração do SQLite Windows |
| `Coletar-Logs.ps1` | coleta evidências em ZIP |
| `Instalar-Painel-Como-Servico.ps1` | instala serviço Windows |
| `Remover-Painel-Servico.ps1` | remove serviço Windows |
| `Instalar-Homologacao-Central.ps1` | fluxo legado de hospedagem Windows |
| `Validar-Homologacao-Central.ps1` | validação pública do pacote Windows |

## Dados

- banco: `data/controlplane.db`;
- configuração: `appsettings.Local.json`;
- logs: `logs/`;
- chaves: subdiretório `keys` junto ao banco.

Esses itens contêm dados sensíveis e não devem ser enviados ao Git.

## Limites

- não configure Cloudflare Tunnel para a stack atual da Contabo;
- não execute uma segunda instância contra o mesmo SQLite;
- não use este pacote como atalho para criar ControlPlane por cliente;
- para a VM, siga [deploy/portainer/README.md](../portainer/README.md).
