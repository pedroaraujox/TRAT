# Debug Session: agent-service-start-fail

- Status: OPEN
- StartedAt: 2026-06-17
- Symptom: `Start-Service` falha ao iniciar `WebstationBackupAgent`.
- Scope: instalacao local do Agent como servico Windows.
- Constraint: nao alterar logica de negocio antes de coletar evidencia de runtime.

## Hypotheses
- H1: o servico falha no bootstrap por nao encontrar `agent.settings.json` ou `project.rules.json` no caminho esperado em `ProgramData`.
- H2: o servico inicia como `LocalSystem` e perde acesso as credenciais AWS do `aws configure`, gerando falha no precheck ou no bootstrap.
- H3: o servico falha por dependencia/runtime do .NET Framework ou permissao de leitura em `Program Files` / `ProgramData`.
- H4: o binario do servico sobe, mas encerra imediatamente por excecao nao observada no SCM; a evidência deve aparecer em Event Viewer ou no `agent.log.jsonl`.
- H5: o servico foi criado corretamente, mas o contexto de execucao nao consegue acessar `ControlPlaneBaseUrl` ou outro recurso local e morre durante a inicializacao.

## Evidence Log
- `sc qc WebstationBackupAgent`: servico instalado como `LocalSystem`, binario em `C:\Program Files\WebstationBackup\Agent\WebstationBackup.Agent.Service.exe`.
- `C:\ProgramData\WebstationBackup\Agent`: contem `agent.settings.json` e `project.rules.json`, entao H1 foi rejeitada.
- Event Viewer `.NET Runtime` ID `1026`: excecao sem tratamento `System.InvalidOperationException` em `System.ServiceProcess.ServiceBase.set_ServiceName(String)`.
- Stack da excecao aponta para `WebstationBackup.Agent.Service.Service.BackupWindowsService.OnStart(String[])`.
- `Application Error` ID `1000`: confirma encerramento do processo do servico durante a inicializacao.
- Tentativa posterior de reinstalacao com `-ServiceCredential`: falhou antes da instalacao porque o pacote descompactado do usuario estava com os binarios na raiz, enquanto o script esperava `bin\`.
- Execucao direta do pacote em `Run-Agent-DryRun.cmd` inicialmente falhou com `FileNotFoundException` para `C:\ProgramData\WebstationBackup\Agent\agent.log.jsonl`; a causa operacional foi o launcher nao informar `--state-dir`, forcando uso de `ProgramData`.
- Post-fix: `Run-Agent-DryRun.cmd` passou a usar `runtime-state` dentro do proprio pacote e terminou com codigo `0`.
- Post-fix log: readiness AWS validado em modo somente leitura, `Config report enviado` e `Manifest gerado`.

## Hypothesis Status
- H1: rejeitada.
- H2: ainda nao testada; fica para depois da correcao do crash inicial.
- H3: parcialmente rejeitada; o crash atual nao e de dependencias do .NET, e de uso incorreto da API do servico.
- H4: confirmada.
- H5: ainda nao testada; fica para depois da correcao do crash inicial.
- H6: o instalador do Agent assumia apenas layout com `bin\`; confirmada e corrigida para aceitar pacote achatado na raiz.
- H7: o launcher de dry-run dependia implicitamente de `ProgramData`; confirmada e corrigida com `--state-dir` local ao pacote.

## Next Step
- Correcao aplicada: `ServiceName` movido para o construtor de `BackupWindowsService`, evitando o `InvalidOperationException` durante `OnStart`.
- Validacao local: `dotnet build` e `dotnet test` concluidos com sucesso; pacote do Agent regenerado e copiado para a pasta descompactada usada pelo usuario.
- Correcao aplicada no instalador: suporte a pacote com `bin\` e tambem a pacote achatado com `.exe` na raiz.
- Correcao aplicada no launcher simples: `Run-Agent-DryRun.cmd` cria `runtime-state` e executa com `--state-dir` local, sem depender de instalacao nem de `ProgramData`.
- Proxima verificacao pendente: usuario executar o launcher simples no proprio host e confirmar o comportamento observado.
