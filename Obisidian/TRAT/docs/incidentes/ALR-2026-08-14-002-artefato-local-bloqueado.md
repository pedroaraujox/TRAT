# ALR-2026-08-14-002 — Artefato local bloqueado

- Severidade: alarme operacional
- Estado: aberto
- Ambiente: desenvolvimento local

## Sintoma

`Publish-Agent.ps1` não conseguiu remover `artifacts\agent-package-local\TRAT.Agent.Package.zip` porque outro processo mantém o arquivo aberto.

## Resposta segura

Não forçar exclusão, não encerrar processos indiscriminadamente e não ampliar o alvo. Fechar Explorer, compactadores ou processos que estejam usando o ZIP e aguardar o OneDrive/antivírus liberar o arquivo. Enquanto isso, gerar o pacote em um diretório de verificação novo por meio de `-PackageOutputDir`.

## Encerramento

O alarme encerra quando o pacote canônico `local` puder ser regenerado normalmente e o manifesto, Setup e ZIP forem validados.
