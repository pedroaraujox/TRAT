# ALR-2026-08-14-001 — Docker fora do PATH

- Severidade: alarme operacional
- Estado: conhecido
- Ambiente: desenvolvimento local

## Sintoma

`docker` não é reconhecido pela sessão, embora o Docker Desktop esteja instalado por usuário.

## Diagnóstico e ação

Verifique `%LOCALAPPDATA%\Programs\DockerDesktop\resources\bin\docker.exe`. Use o caminho absoluto durante a validação ou corrija o `PATH` da sessão. Não interprete esse sintoma isoladamente como ausência do Docker Desktop.

## Prevenção

Os scripts locais devem localizar explicitamente a CLI por usuário antes de declarar o Docker indisponível.
