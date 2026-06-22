# Painel Local

Pacote local do `ControlPlane` para uso interno, sem publicar na internet.

## Como usar

1. Copie a pasta do pacote para a maquina desejada.
2. Dê duplo clique em `Iniciar-Painel-Local.cmd`.
3. Na primeira execucao, informe:
   - e-mail do administrador;
   - nome do administrador;
   - senha inicial.
4. Acesse `http://localhost:5080`.

## O que o pacote faz

- sobe o painel localmente com banco SQLite em `data\controlplane.db`;
- cria `appsettings.Local.json` na primeira execucao;
- grava logs de runtime em `logs\controlplane.stdout.log` e `logs\controlplane.stderr.log`;
- tenta reutilizar `artifacts\agent-package` para a pagina de downloads do Agent.

## Arquivos importantes

- `Iniciar-Painel-Local.cmd`: inicia o painel e abre o navegador.
- `Parar-Painel-Local.ps1`: encerra o processo do painel iniciado pelo pacote.
- `Backup-Painel-Dados.ps1`: gera uma copia manual do banco SQLite do painel.
- `Primeira-Configuracao.ps1`: gera a configuracao local inicial.
- `appsettings.Local.json`: sobrescritas locais do ambiente.

## Observacoes operacionais

- nao copie o pacote para pastas sincronizadas por OneDrive sem validar bloqueios de arquivo;
- mantenha backup de `data\controlplane.db`;
- proteja `appsettings.Local.json`, porque ele contem segredo administrativo e senha bootstrap;
- execute `Backup-Painel-Dados.ps1` antes de atualizacoes do painel ou mudancas relevantes;
- se quiser trocar a senha bootstrap depois, use a area administrativa do painel e remova o valor de bootstrap do arquivo local quando estabilizar.
