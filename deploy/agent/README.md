# Pacote do Agent

## Conteudo
- `agent.settings.template.json`: modelo de configuracao local do host.
- `project.rules.json`: regras operacionais do agent.
- `Install-Agent.ps1`: instala ou atualiza o agent como servico Windows.
- `Uninstall-Agent.ps1`: remove o servico Windows do agent.
- `bin\`: binarios do `WebstationBackup.Agent.Service`.
- `tray\`: aplicativo de bandeja do Windows para status local do agent.
- `installer\`: instalador GUI para configurar o agent e disparar a instalacao com elevacao.
- `Launch-Agent-Tray.cmd`: abre o `Tray App` diretamente do pacote.
- `Launch-Agent-Installer.cmd`: abre o instalador GUI diretamente do pacote.

## Diretórios recomendados
- Binarios: `C:\Program Files\WebstationBackup\Agent`
- Estado e configuracao: `C:\ProgramData\WebstationBackup\Agent`

## Fluxo seguro
1. Execute `Launch-Agent-Installer.cmd`.
2. Preencha os campos do host, valide e salve `agent.settings.json`.
3. Instale o agent pela GUI; a elevacao administrativa sera solicitada apenas nessa etapa.
4. Rode primeiro em modo console/dry-run antes de iniciar o servico real.
5. So habilite execucao real com upload apos validacao conjunta.
6. Execute `Launch-Agent-Tray.cmd` para deixar o icone do agent na bandeja do Windows.

## Regra operacional
- Apos qualquer mudanca de codigo, configuracao ou empacotamento, reexecute o `ControlPlane` antes de validar o ambiente.
- No host afetado, rode novamente o `Agent` em `dry-run` antes de iniciar ou liberar o servico real.
- Nao considere a mudanca validada sem rechecagem de painel, onboarding, heartbeat e logs locais.

## Segredos
- O instalador salva o `AgentToken` como `AgentTokenDpapiProtected` (DPAPI LocalMachine) por padrao, evitando token em texto puro no arquivo.
- O servico consegue descriptografar esse token no proprio host, mesmo rodando como `LocalSystem` ou outro usuario local.

## Arquitetura recomendada
- O `Windows Service` executa backup, heartbeat, prechecks e upload em segundo plano.
- O `Tray App` mostra status local, abre o painel e facilita suporte sem depender de janela aberta.
- Fechar a janela do `Tray App` apenas oculta a interface; o icone continua na bandeja.

## Observacao sobre credenciais AWS
- `aws configure` normalmente grava credenciais no perfil do seu usuario (nao no LocalSystem).
- Se o servico rodar como LocalSystem, o precheck AWS pode falhar mesmo que o `aws configure` do seu usuario esteja OK.
- Para teste real como servico, prefira instalar o servico com `-ServiceCredential` apontando para um usuario que tenha acesso as credenciais (ou use Windows Credential Manager conforme politica do produto).
