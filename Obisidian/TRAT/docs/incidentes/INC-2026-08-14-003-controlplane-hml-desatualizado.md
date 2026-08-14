# INC-2026-08-14-003 — ControlPlane de homologação desatualizado

- Severidade: SEV-2
- Estado: resolvido
- Ambiente: homologação

## Sintoma e impacto

`/api/v1/health` respondia HTTP 200, mas `/api/v1/environment`, presente na branch `development`, respondia HTTP 404. O healthcheck sozinho gerava falsa impressão de atualização concluída.

## Causa raiz

A imagem ativa na stack não correspondia ao HEAD de `development`; o pipeline publica a imagem, mas não executa automaticamente o redeploy no Portainer.

## Correção aplicada

- expor ambiente e revisão Git em `/api/v1/environment`;
- gravar a revisão na imagem durante o pipeline;
- exigir conferência da revisão após o redeploy;
- manter tag por SHA para rollback.

## Validação

Em 14/08/2026, após o redeploy controlado do stack `trat-dev`:

- `/api/v1/health` retornou `{"status":"ok"}`;
- `/api/v1/environment` retornou `name: hml`, a URL pública de homologação e a revisão `87fc9106694a6fb2960c39e7238348244ba8478c`;
- o login administrativo funcionou;
- os dados preexistentes permaneceram disponíveis;
- a página de downloads exibiu pacote disponível, ambiente `hml` e destino `https://trat-hml.outboxtech.com.br`.

A validação do Agent instalado permanece registrada separadamente em INC-2026-08-14-001.

## Rollback

Reimplantar a tag SHA anterior sem remover os volumes `data` e `backups`.
