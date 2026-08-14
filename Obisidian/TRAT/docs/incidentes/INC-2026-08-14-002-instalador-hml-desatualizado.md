# INC-2026-08-14-002 — Instalador de homologação desatualizado

- Severidade: SEV-2
- Estado: correção em andamento
- Ambiente: homologação

## Sintoma e impacto

Reinstalar o Agent a partir do painel não entregava a correção de descoberta AWS.

## Evidências

O artefato `hml` havia sido gerado em 2026-08-12, enquanto o Agent corrigido foi gerado apenas no perfil `local` em 2026-08-13. Os executáveis possuíam hashes e tamanhos diferentes.

## Causa raiz

As alterações permaneceram no working tree. Não houve push em `development`, publicação da imagem nem redeploy da stack de homologação.

## Correção planejada

Publicar `development`, validar o GitHub Actions, atualizar `trat-dev` preservando volumes e comparar o manifesto baixado do painel com o commit implantado.

## Prevenção

Build local não encerra uma correção distribuída. O gate exige download do instalador pelo painel e validação do manifesto, URL, hash e fluxo real.
