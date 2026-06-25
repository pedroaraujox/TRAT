# Pacote do TRAT Agent

## Conteudo
- `agent.settings.template.json`: modelo de configuracao local do host.
- `project.rules.json`: regras operacionais do agent.
- `Install-Agent.ps1`: instala ou atualiza o agent como servico Windows.
- `Uninstall-Agent.ps1`: remove o servico Windows do agent.
- `Update-Agent.ps1`: aplica atualizacao do agent a partir de um pacote ja baixado, preservando a configuracao local.
- `bin\`: binarios do servico do TRAT Agent.
- `tray\`: aplicativo de bandeja do Windows para status local do agent.
- `installer\`: instalador GUI para configurar o agent e disparar a instalacao com elevacao.
- `Launch-Agent-Tray.cmd`: abre o `Tray App` diretamente do pacote.
- `Launch-Agent-Installer.cmd`: abre o instalador GUI diretamente do pacote.

## Diretórios recomendados
- Binarios: `C:\Program Files\TRAT\Agent` ou pasta operacional equivalente
- Estado e configuracao: `C:\ProgramData\TRAT\Agent` ou pasta operacional equivalente

## Fluxo seguro
1. Execute `Launch-Agent-Installer.cmd`.
2. Preencha os campos do host, valide e salve `agent.settings.json`.
3. Instale o agent pela GUI; a elevacao administrativa sera solicitada apenas nessa etapa.
4. O instalador cria atalhos no menu Iniciar, registra desinstalacao no Windows e pode iniciar o servico ao final.
5. Rode primeiro em modo console/dry-run antes de iniciar o servico real.
6. So habilite execucao real com upload apos validacao conjunta.
7. O `Tray App` passa a funcionar como aplicativo instalado do TRAT; atualizacoes devem substituir a versao anterior sem exigir fechamento manual do icone oculto.

## Regra operacional
- Antes de validar qualquer alteracao, siga tambem o checklist global em `docs\CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md`.
- Apos qualquer mudanca de codigo, configuracao ou empacotamento, reexecute o `ControlPlane` antes de validar o ambiente.
- Apos qualquer mudanca em `src\Agent`, `scripts\agent`, `deploy\agent` ou no fluxo de download do Agent, regenere o pacote com `scripts\agent\Publish-Agent.ps1` antes de testar instalacao, update ou download pelo painel.
- Se o painel estiver distribuindo o Agent pela pagina de downloads, regenere tambem o pacote local do painel com `scripts\controlplane\Publish-ControlPlane.ps1` para embutir os artifacts atualizados.
- Nao considere a alteracao pronta enquanto a pagina `/admin/downloads` nao servir novamente o ZIP e o Setup atualizados do Agent.
- No host afetado, rode novamente o `Agent` em `dry-run` antes de iniciar ou liberar o servico real.
- Nao considere a mudanca validada sem rechecagem de painel, onboarding, heartbeat e logs locais.

## Segredos
- O instalador salva o `AgentToken` como `AgentTokenDpapiProtected` (DPAPI LocalMachine) por padrao, evitando token em texto puro no arquivo.
- O servico consegue descriptografar esse token no proprio host, mesmo rodando como `LocalSystem` ou outro usuario local.
- O instalador tambem pode salvar a credencial AWS local como `AwsCredentialDpapiProtected` (DPAPI LocalMachine), permitindo que o servico valide e execute upload mesmo quando roda como `LocalSystem`.

## Arquitetura recomendada
- O `Windows Service` executa backup, heartbeat, prechecks e upload em segundo plano.
- O `Tray App` mostra status local, abre o painel e facilita suporte sem depender de janela aberta.
- Fechar a janela do `Tray App` apenas oculta a interface; o icone continua na bandeja.
- A instalacao registra atalhos do menu Iniciar e entrada de desinstalacao para o host se comportar como aplicativo instalado de verdade.

## Observacao sobre credenciais AWS
- `aws configure` normalmente grava credenciais no perfil do seu usuario (nao no LocalSystem).
- Se o servico rodar como LocalSystem, o precheck AWS pode falhar mesmo que o `aws configure` do seu usuario esteja OK.
- Para o MVP atual, prefira informar `AWS Access Key` e `AWS Secret Key` diretamente no instalador GUI; elas serao gravadas protegidas por maquina via DPAPI e o servico conseguira usa-las sem depender do perfil do usuario.
- O uso de `Windows Credential Manager` ou de uma conta de servico dedicada continua compativel, mas deixa de ser obrigatorio para o teste real do MVP.
