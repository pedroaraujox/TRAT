# Debug Session: agent-download-package

- Status: OPEN
- StartedAt: 2026-06-19
- Symptom: ao baixar/instalar o Agent diretamente pelo painel, o instalador inicia com `Pasta inicial do pacote: C:\Users\Pedro Araujo\Downloads\` e falha na validacao informando ausencia de `Install-Agent.ps1`, executavel do servico e `project.rules.json`.
- Scope: distribuicao do pacote do Agent pelo `ControlPlane`, UX do instalador GUI e deteccao do layout do pacote.

## Hypotheses

1. O link de download do painel está entregando apenas o `Setup.exe`, mas o instalador ainda exige a pasta completa do pacote ao lado do executável.
2. O `Setup.exe` baixado pelo painel não consegue detectar automaticamente que está rodando sem os artefatos auxiliares e por isso usa `Downloads\` como `PackageRoot`.
3. O catálogo do painel está apontando para um artefato incompleto ou desatualizado em `artifacts`.
4. O instalador GUI não está preparado para o modo "standalone via painel" e continua validando apenas o fluxo antigo baseado em pasta de pacote.
5. O nome/estrutura servidos pelo painel estão corretos, mas o usuário executou o binário fora da pasta do pacote e a validação não orienta adequadamente esse cenário.

## Evidence Log

- O log do usuário já mostra evidência de runtime suficiente:
  - `Pasta inicial do pacote: C:\Users\Pedro Araujo\Downloads\`
  - erros de validação de ausência de `Install-Agent.ps1`, executável do serviço e `project.rules.json`.
- Em [InstallerPackagePaths.cs](file:///c:/Users/Pedro%20Araujo/Desktop/PROJETOS%20EM%20DESENVOLVIMENTO/TRAT/src/Agent/WebstationBackup.Agent.Installer/InstallerPackagePaths.cs#L28-L38), o instalador usa `AppContext.BaseDirectory` como fallback quando não encontra um pacote completo.
- Em [InstallerValidation.cs](file:///c:/Users/Pedro%20Araujo/Desktop/PROJETOS%20EM%20DESENVOLVIMENTO/TRAT/src/Agent/WebstationBackup.Agent.Installer/InstallerValidation.cs#L27-L40), a validação exige explicitamente `Install-Agent.ps1`, `bin\WebstationBackup.Agent.Service.exe` e `project.rules.json`.
- Em [Publish-Agent.ps1](file:///c:/Users/Pedro%20Araujo/Desktop/PROJETOS%20EM%20DESENVOLVIMENTO/TRAT/scripts/agent/Publish-Agent.ps1#L57-L72), o pacote completo existe como pasta estruturada e também como `WebstationBackup.Agent.Package.zip`.
- Em [Downloads.cshtml](file:///c:/Users/Pedro%20Araujo/Desktop/PROJETOS%20EM%20DESENVOLVIMENTO/TRAT/src/ControlPlane/ControlPlane.Api/Views/PanelAdmin/Downloads.cshtml#L28-L41), a UI promovia `Baixar Setup.exe` como ação principal, embora o fluxo real ainda dependa do pacote completo.

## Hypothesis Status

1. O link de download do painel está entregando apenas o `Setup.exe`, mas o instalador ainda exige a pasta completa do pacote ao lado do executável. -> Confirmada.
2. O `Setup.exe` baixado pelo painel não consegue detectar automaticamente que está rodando sem os artefatos auxiliares e por isso usa `Downloads\` como `PackageRoot`. -> Confirmada.
3. O catálogo do painel está apontando para um artefato incompleto ou desatualizado em `artifacts`. -> Rejeitada.
4. O instalador GUI não está preparado para o modo "standalone via painel" e continua validando apenas o fluxo antigo baseado em pasta de pacote. -> Confirmada.
5. O nome/estrutura servidos pelo painel estão corretos, mas o usuário executou o binário fora da pasta do pacote e a validação não orienta adequadamente esse cenário. -> Confirmada.

## Next Steps

1. Corrigir a UX do painel para priorizar o pacote completo (`ZIP`) como fluxo recomendado.
2. Ajustar a mensagem de validação do instalador para orientar explicitamente o cenário de `Setup.exe` isolado.
3. Rebuildar painel e instalador.
4. Solicitar nova validação do fluxo de download pelo painel.
