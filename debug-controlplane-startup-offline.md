# Debug Session: controlplane-startup-offline

Status: OPEN

## Symptom
- `.\Iniciar-TRAT.ps1` foi executado, mas `http://localhost:5080` nao respondeu.

## Scope
- Startup do ControlPlane em modo local/servico.

## Hypotheses
- H1: O script retornou sucesso, mas o servico `TRATControlPlane` nao iniciou ou caiu imediatamente.
- H2: O servico iniciou, mas a aplicacao falhou no bootstrap e encerrou antes de abrir a porta 5080.
- H3: O script caiu em modo local, mas o processo `ControlPlane.Api.exe` nao permaneceu vivo.
- H4: A aplicacao esta rodando, mas nao esta bindando em `localhost:5080` por conflito/configuracao.
- H5: O painel subiu, mas houve erro de permissao/estado local (`appsettings.Local.json`, banco, keys, logs) impedindo resposta HTTP.

## Evidence Log
- Pre-fix: `powershell.exe -ExecutionPolicy Bypass -File .\Iniciar-TRAT.ps1 -NoBrowser` falhou com `NamedParameterNotFound` informando parametro `and`.
- A linha com falha foi `if (Test-Path $installServiceScript -and (Test-IsAdministrator))`, onde `-and` foi interpretado como parametro do `Test-Path`.
- `controlplane.stderr.log` estava vazio; o problema ocorreu antes de tentar subir o processo do painel.
- Fix aplicado: `if ((Test-Path $installServiceScript) -and (Test-IsAdministrator))`.
- Post-fix: `.\Iniciar-TRAT.ps1 -NoBrowser` iniciou o painel com sucesso.
- Post-fix: `Get-NetTCPConnection -LocalPort 5080 -State Listen` retornou listeners em `::1:5080` e `127.0.0.1:5080`.
- Post-fix: `Invoke-WebRequest 'http://localhost:5080'` retornou `200`.
- Post-fix: `controlplane.stdout.log` registrou `Now listening on: http://localhost:5080`.

## Next Steps
- Solicitar validacao do usuario. Se confirmado, encerrar sessao de debug e remover artefatos de debug.
