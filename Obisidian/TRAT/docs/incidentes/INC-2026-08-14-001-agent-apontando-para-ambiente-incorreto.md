# INC-2026-08-14-001 — Agent apontando para ambiente incorreto

- Severidade: SEV-2
- Estado: correção em andamento
- Ambientes: local e homologação

## Sintoma e impacto

O serviço `TRAT Agent` executava normalmente, mas o `agent.settings.json` apontava para `http://localhost:5080` sem um ControlPlane local ativo. Heartbeat, configuração AWS e jobs não chegavam à homologação.

## Evidências

- serviço Windows em estado `Running`;
- binário instalado correspondente ao build corrigido;
- URL instalada igual a `http://localhost:5080`;
- log repetindo `HttpRequestException` e `Execução do job falhou`;
- porta local 5080 sem serviço.

## Causa raiz

Ao reinstalar, o instalador carregava integralmente a configuração existente e substituía a URL declarada pelo pacote. Isso permitia que um pacote de homologação preservasse silenciosamente a URL de outro ambiente.

## Correção planejada

- a URL e o ambiente vêm sempre do manifesto do pacote;
- identidade, token, credenciais AWS e pastas podem ser reaproveitados;
- o instalador informa quando descarta uma URL instalada incompatível;
- validar os três perfis antes da distribuição.

## Validação e rollback

Instalar cada pacote sobre uma configuração de outro ambiente e confirmar a URL final. Em rollback, reinstalar o último pacote aprovado do mesmo ambiente; nunca copiar settings entre ambientes.
