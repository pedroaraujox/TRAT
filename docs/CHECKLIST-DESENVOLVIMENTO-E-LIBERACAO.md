# Checklist de Desenvolvimento e Liberacao

Este documento define as regras operacionais do TRAT para desenvolvimento, validacao e liberacao de artefatos.

## Regra principal

- Qualquer alteracao no projeto exige rebuild ou republish dos artefatos afetados antes de validar, testar ou liberar.
- Nenhuma mudanca e considerada pronta apenas porque compilou no codigo-fonte; a validacao deve acontecer no artefato real usado por operador, host ou cliente.
- Para o fluxo padrao do dia a dia, prefira executar `.\Liberar-TRAT.ps1`.
- **Preservar estado/dados de teste sempre que possivel**: ao validar uma mudanca, priorize atualizar o Agent/ControlPlane ja instalados (update in-place) em vez de reinstalar do zero ou recriar cliente/host no painel. So reinstalar ou recriar cadastro quando isso for tecnicamente a unica forma de validar a mudanca (ex.: mudanca no proprio fluxo de instalacao/onboarding).
- O ControlPlane e central. Servidores de clientes recebem apenas o Agent.
- Nenhum ControlPlane internet-facing pode escutar em interface publica; a origem deve permanecer em loopback atras do Tunnel HTTPS.

## Regras gerais

- Sempre rebuildar o que foi afetado:
  - alterou `Agent` -> executar `scripts\agent\Publish-Agent.ps1`;
  - alterou `ControlPlane` -> executar `scripts\controlplane\Publish-ControlPlane.ps1`;
  - alterou ambos ou alterou integracao entre ambos -> executar os dois publishes.
- Sempre validar pelo artefato final:
  - nunca validar somente por `bin`, `obj`, execucao via IDE ou arquivo solto;
  - validar ZIP, Setup, pacote local ou servico realmente distribuido.
- Sempre revalidar o fluxo impactado:
  - UI -> abrir a tela real e conferir comportamento;
  - download -> baixar novamente do painel;
  - install/update -> rodar o instalador/update real;
  - servico -> iniciar no modo real e validar operacao;
  - backup -> executar ou agendar e observar o resultado no painel.
- Sempre manter compatibilidade operacional:
  - scripts do Agent devem continuar compativeis com PowerShell 5.1;
  - o ControlPlane deve continuar funcional como Windows Service;
  - nenhuma mudanca deve depender de ambiente ideal ou apenas da maquina de desenvolvimento.
- Sempre produzir evidencia minima:
  - build/publish executado;
  - teste automatizado relevante executado;
  - smoke test do fluxo alterado executado;
  - resultado registrado antes de considerar a tarefa pronta;
  - `release report` gerado pelo `.\Liberar-TRAT.ps1`.

## Regras por area

### Agent

- Toda mudanca em `src\Agent`, `scripts\agent`, `deploy\agent` ou no fluxo de download do Agent exige `scripts\agent\Publish-Agent.ps1`.
- Se o painel estiver distribuindo o Agent, tambem e obrigatorio regenerar o pacote do ControlPlane para embutir os novos artifacts.
- Nenhuma mudanca do Agent e considerada pronta sem validar:
  - pagina `/admin/downloads`;
  - download do ZIP do Agent;
  - download do Setup do Agent;
  - instalacao ou update do Agent;
  - `dry-run`, heartbeat e reflexo no painel.

### ControlPlane

- Toda mudanca em `src\ControlPlane`, `scripts\controlplane`, `deploy\controlplane`, autenticacao, assets, banco, configuracao ou downloads exige `scripts\controlplane\Publish-ControlPlane.ps1`.
- Nenhuma mudanca do ControlPlane e considerada pronta sem validar:
  - inicializacao do painel no pacote local;
  - login administrativo;
  - dashboard;
  - rota ou funcionalidade alterada;
  - persistencia basica de dados quando aplicavel.

### Integracao Agent <-> ControlPlane

- Toda mudanca de contrato, telemetria, heartbeat, job, configuracao, download, onboarding ou instalacao exige validacao ponta a ponta.
- Nao fechar tarefa de integracao sem validar no minimo:
  - cadastro/configuracao;
  - sincronizacao;
  - heartbeat;
  - execucao manual ou agendada;
  - status final no painel.
  - interrupcao e retomada de internet quando a mudanca afetar telemetria;
  - TLS 1.2 em Windows Server 2012/2012 R2 quando a mudanca afetar rede ou instalacao.

### Homologacao central

- Gerar `scripts\controlplane\Publish-Homologacao-Central.ps1`.
- Validar `Validar-Homologacao-Central.ps1` contra o hostname HTTPS.
- Confirmar que `/api/v1/admin` retorna 404 quando nao foi explicitamente habilitada.
- Confirmar cookies Secure/HttpOnly, CSP, HSTS, host filtering e rate limiting.
- Executar backup consistente com o servico parado e ensaiar restauracao.
- Validar retorno automatico dos servicos `TRATControlPlane` e `cloudflared` apos reboot.

## Gates de saida

- Nao considerar pronto se o pacote distribuido estiver desatualizado.
- Nao considerar pronto se o painel estiver servindo arquivo antigo.
- Nao considerar pronto se a validacao ocorreu apenas no repositorio e nao no artefato final.
- Nao considerar pronto se faltarem logs, evidencias ou reproducao minima do fluxo alterado.
- Nao considerar pronto se a mudanca quebrar operacao headless, reboot resilience ou compatibilidade com Windows corporativo.
- Nao considerar pronto se o `release report` final estiver com status `INCOMPLETE` ou `FAILED`.

## Sequencia minima recomendada

1. Alterar o codigo.
2. Executar build/testes da area afetada.
3. Regenerar os artifacts afetados.
4. Regerar o pacote do painel quando ele embutir artifacts atualizados.
5. Validar o fluxo real no artefato final.
6. Registrar o resultado.
7. So entao considerar a tarefa pronta.

## Comando padrao

- Fluxo completo com publish e smoke test: `.\Liberar-TRAT.ps1`
- Pular publish do Agent: `.\Liberar-TRAT.ps1 -SkipAgentPublish`
- Pular publish do ControlPlane: `.\Liberar-TRAT.ps1 -SkipControlPlanePublish`
- Rodar apenas publish sem smoke test: `.\Liberar-TRAT.ps1 -SkipSmokeTest -AllowIncompleteRelease`
- Forcar reset de estado local antes da validacao: `.\Liberar-TRAT.ps1 -ResetLocalState`
- Toda execucao gera `release report` em `artifacts\release-reports`.
