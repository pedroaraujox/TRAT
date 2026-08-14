# INC-2026-08-14-005 — Builds de ambientes compartilhavam saída

- Severidade: SEV-2
- Estado: corrigido no código, aguardando validação
- Ambientes: local, homologação e produção

## Sintoma

Ao gerar os perfis em sequência, `GenerateBundle` falhou porque `WebstationBackup.Agent.Installer.exe` na pasta comum de publish estava em uso.

## Causa raiz

Todos os perfis reutilizavam `bin\Release\...\publish`. Sincronização, antivírus ou outro leitor podia manter o executável anterior aberto, e um perfil também podia consumir resíduo do outro.

## Correção

Cada execução de `Publish-Agent.ps1` passa a usar diretórios intermediários próprios dentro de seu `PackageOutputDir` para Service, Tray e Installer.

## Validação

Gerar `local`, `hml` e `production` em sequência e confirmar ambiente, URL e executáveis independentes em cada manifesto.
