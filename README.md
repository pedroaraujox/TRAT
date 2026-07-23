# TRAT

**Status atual: desenvolvimento 100% local.** Não há publicação em VPS/nuvem neste momento — todo o ciclo de build, teste e uso acontece na própria máquina Windows de desenvolvimento.

## Como rodar

Guia completo (dependências, passo a passo, troubleshooting): [docs/COMO-RODAR-LOCALMENTE.md](docs/COMO-RODAR-LOCALMENTE.md)

Resumo rápido:
```powershell
.\Liberar-TRAT.ps1   # builda Agent + ControlPlane, roda smoke test, gera release report
.\Iniciar-TRAT.ps1   # sobe o painel em http://localhost:5080
.\Parar-TRAT.ps1     # para o painel
```

## Regra operacional geral

- Qualquer alteracao no projeto exige rebuild ou republish dos artefatos afetados antes de validar.
- Nenhuma mudanca e considerada pronta apenas porque compilou no codigo-fonte; a validacao deve acontecer no artefato real distribuido.
- Checklist operacional completo: `docs\CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md`.
- Fluxo unico recomendado: `.\Liberar-TRAT.ps1`.
- Evidencia obrigatoria: `.\Liberar-TRAT.ps1` gera `release report` em `artifacts\release-reports`.

Se quiser reiniciar o teste local do zero sem reaproveitar banco, tokens e configuracao anteriores:

- execute `Resetar-TRAT.ps1`; ou
- execute `Iniciar-TRAT.ps1 -ResetLocalState`.

O reset faz backup de seguranca do `appsettings.Local.json` e do `controlplane.db` antes de limpar o estado local do pacote.
