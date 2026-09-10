# Registros de decisão arquitetural

ADRs preservam contexto, decisão e consequências de escolhas importantes.

| ADR | Estado | Decisão |
| --- | --- | --- |
| [0001](0001-controlplane-central-docker-sqlite.md) | aceita | ControlPlane central em Docker com SQLite |
| [0002](0002-mvp-somente-development.md) | substituída | MVP somente na stack development |
| [0003](0003-promocao-local-hml-production.md) | aceita | promoção local, homologação e produção |
| [0004](0004-agent-minimo-controlado-pelo-controlplane.md) | aceita | Agent recebe somente token e credenciais AWS; operação fica no ControlPlane |

Novas decisões relevantes devem receber número sequencial e não reescrever decisões históricas; uma decisão substituída ganha novo ADR.
