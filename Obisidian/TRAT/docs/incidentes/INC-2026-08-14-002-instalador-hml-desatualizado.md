# INC-2026-08-14-002 — Instalador de homologação desatualizado

- Severidade: SEV-2
- Estado: resolvido
- Ambiente: homologação

## Sintoma e impacto

Reinstalar o Agent a partir do painel não entregava a correção de descoberta AWS.

## Evidências

O artefato `hml` havia sido gerado em 2026-08-12, enquanto o Agent corrigido foi gerado apenas no perfil `local` em 2026-08-13. Os executáveis possuíam hashes e tamanhos diferentes.

## Causa raiz

As alterações permaneceram no working tree. Não houve push em `development`, publicação da imagem nem redeploy da stack de homologação.

## Correção aplicada

- publicada a revisão `87fc9106694a6fb2960c39e7238348244ba8478c` em `development`;
- pipeline de testes, pacote do Agent e imagem do ControlPlane concluído com sucesso;
- stack `trat-dev` atualizado preservando volumes;
- página autenticada de downloads validada com pacote publicado em 14/08/2026, ambiente `hml` e destino `https://trat-hml.outboxtech.com.br`;
- download do `TRAT.Agent.Setup.exe` acionado pelo painel.

O teste do serviço Agent instalado e seu heartbeat é acompanhado no INC-2026-08-14-001, pois a instalação ativa da máquina local não deve trocar de ambiente silenciosamente.

## Prevenção

Build local não encerra uma correção distribuída. O gate exige download do instalador pelo painel e validação do manifesto, URL, hash e fluxo real.
