# Ambientes do TRAT

## Desenvolvimento local

- Perfil do Agent: `local`
- ControlPlane: `http://localhost:5080`
- Banco: volume Docker `trat_local_data`, exclusivo da maquina local
- Artefatos: `artifacts/agent-package-local`
- Geracao: `powershell -File .\scripts\agent\Publish-Agent.ps1 -EnvironmentProfile local`
- Validacao: `http://localhost:5080/api/v1/environment` deve retornar `name: local`

## Homologacao

- Perfil do Agent: `hml`
- ControlPlane: `https://trat-hml.outboxtech.com.br`
- Banco: volume da stack `trat-hml`, separado do ambiente local
- Artefatos: `artifacts/agent-package-hml`
- Geracao: `powershell -File .\scripts\agent\Publish-Agent.ps1 -EnvironmentProfile hml`
- Validacao: `https://trat-hml.outboxtech.com.br/api/v1/environment` deve retornar `name: hml`

## Producao

- Perfil do Agent: `production`
- ControlPlane: `https://trat.outboxtech.com.br`
- Banco: volume da stack `trat-prod`, isolado de local e homologacao
- Artefatos: `artifacts/agent-package-production`
- Geracao: `powershell -File .\scripts\agent\Publish-Agent.ps1 -EnvironmentProfile production`
- Origem: somente merge revisado de `development` para `production`
- Validacao: `https://trat.outboxtech.com.br/api/v1/environment` deve retornar `name: production` e o SHA promovido

## Regras de seguranca operacional

- Tokens pertencem ao banco do ambiente em que foram gerados e nao sao intercambiaveis.
- O instalador sempre mostra `Destino do painel` antes da instalacao.
- Todo pacote possui `agent-package.manifest.json` com ambiente e URL.
- O ControlPlane bloqueia o download quando o manifesto do Agent nao corresponde ao seu ambiente e URL.
- O build recusa URLs diferentes das URLs oficiais de cada perfil.
- Homologacao deve aprovar build, testes, instalador, integracao Agent e rollback antes do merge para `production`.
- Nunca compartilhe banco, volume, token, senha ou credencial entre ambientes.
- Use `powershell -File .\Validar-Ambientes.ps1` para validar artefatos dos tres ambientes e endpoints ativos.
