# INC-2026-08-14-006 — Checklist aceitava login falso-positivo

- Severidade: SEV-3
- Estado: correção em andamento
- Ambiente: local e homologação

## Sintoma

`Validar-TRAT.ps1` informava dashboard válido quando a sessão podia ter retornado à tela de login; depois emitia erro genérico na página de downloads.

## Causa raiz

O teste procurava apenas o texto `TRAT`, presente também na tela de login, e dependia do texto visual `TRAT Agent` na página de downloads.

Além disso, credenciais encontradas em um pacote local tinham precedência sobre `-AdminEmail` e `-AdminPassword`, contrariando a intenção explícita do operador.

## Correção

Validar URI final, presença do formulário de login e links estáveis de download. Parâmetros explícitos têm precedência sobre arquivos locais. Falhas devem indicar autenticação separadamente de pacote ausente.

## Validação

Credencial inválida precisa falhar no passo de login; credencial válida deve alcançar `/admin` e baixar Setup/manifesto do ambiente esperado.
