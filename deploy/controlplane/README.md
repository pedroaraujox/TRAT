# ControlPlane central

Pacote autocontido alternativo para hospedar o ControlPlane em Windows. O destino principal usa Contabo/Portainer conforme `deploy\portainer\README.md`. Neste modo alternativo, use `Instalar-Homologacao-Central.ps1` e mantenha a origem em `127.0.0.1:5080` atras do Cloudflare Tunnel.

## Como usar

1. Copie a pasta do pacote para a maquina desejada.
2. Dê duplo clique em `Iniciar-Painel-Local.cmd`.
3. Na primeira execucao, informe:
   - e-mail do administrador;
   - nome do administrador;
   - senha inicial.
4. Acesse `http://localhost:5080`.

O fluxo acima e somente local. Para a arquitetura central, consulte `docs\ARQUITETURA-CENTRAL-E-HOMOLOGACAO.md` no repositorio.

## O que o pacote faz

- sobe o painel localmente com banco SQLite em `data\controlplane.db`;
- cria `appsettings.Local.json` na primeira execucao;
- grava logs de runtime em `logs\controlplane.stdout.log` e `logs\controlplane.stderr.log`;
- tenta reutilizar `artifacts\agent-package` para a pagina de downloads do Agent.

## Arquivos importantes

- `Iniciar-Painel-Local.cmd`: inicia o painel e abre o navegador.
- `Parar-Painel-Local.ps1`: encerra o processo do painel iniciado pelo pacote.
- `Backup-Painel-Dados.ps1`: gera uma copia manual do banco SQLite do painel.
- `Restaurar-Painel-Dados.ps1`: restaura o banco SQLite a partir de um backup.
- `Coletar-Logs.ps1`: coleta evidencias do painel e do agent (se instalado na mesma maquina) e gera um ZIP.
- `Primeira-Configuracao.ps1`: gera a configuracao local inicial.
- `Instalar-Homologacao-Central.ps1`: instala ControlPlane, backup diario e Cloudflare Tunnel.
- `Validar-Homologacao-Central.ps1`: valida HTTPS, login, headers e API administrativa desativada.
- `appsettings.Local.json`: sobrescritas locais do ambiente.
 - `Instalar-Painel-Como-Servico.ps1`: instala o painel como servico Windows com auto-start e restart em falha.
 - `Remover-Painel-Servico.ps1`: remove o servico Windows do painel.

## Observacoes operacionais

- nao copie o pacote para pastas sincronizadas por OneDrive sem validar bloqueios de arquivo;
- mantenha backup de `data\controlplane.db`;
- proteja `appsettings.Local.json`, porque ele contem segredo administrativo e senha bootstrap;
- o painel tenta remover automaticamente a senha bootstrap de `appsettings.Local.json` apos criar/confirmar um admin ativo (para reduzir risco de segredo em texto puro);
- execute `Backup-Painel-Dados.ps1` antes de atualizacoes do painel ou mudancas relevantes;
- se quiser trocar a senha bootstrap depois, use a area administrativa do painel e remova o valor de bootstrap do arquivo local quando estabilizar.

## Execucao como servico (recomendado para servidores)

Para ambientes que reiniciam e precisam voltar automaticamente:

1. Execute `Instalar-Painel-Como-Servico.ps1` como administrador.
2. O servico ficara com startup automatico e politica de restart em falha.
3. Para remover, execute `Remover-Painel-Servico.ps1`.
