# INC-2026-08-14-003 — ControlPlane de homologação desatualizado

- Severidade: SEV-2
- Estado: correção em andamento
- Ambiente: homologação

## Sintoma e impacto

`/api/v1/health` respondia HTTP 200, mas `/api/v1/environment`, presente na branch `development`, respondia HTTP 404. O healthcheck sozinho gerava falsa impressão de atualização concluída.

## Causa raiz

A imagem ativa na stack não correspondia ao HEAD de `development`; o pipeline publica a imagem, mas não executa automaticamente o redeploy no Portainer.

## Correção planejada

- expor ambiente e revisão Git em `/api/v1/environment`;
- gravar a revisão na imagem durante o pipeline;
- exigir conferência da revisão após o redeploy;
- manter tag por SHA para rollback.

## Validação

O endpoint deve retornar `name: hml`, URL pública de homologação e a revisão esperada. Health, login, download do Agent e integração serão testados separadamente.
