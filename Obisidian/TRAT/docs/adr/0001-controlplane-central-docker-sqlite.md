# ADR-0001 — ControlPlane central em Docker com SQLite

- Data: 2026-08-12
- Estado: aceita

## Contexto

O produto precisa centralizar operação e telemetria sem instalar um painel por cliente. A infraestrutura existente oferece Contabo, Docker, Portainer e Nginx Proxy Manager.

## Decisão

Executar um único ControlPlane ASP.NET Core em container Linux, atrás do Nginx Proxy Manager, mantendo SQLite e chaves em volumes persistentes. Instalar somente o Agent Windows nos clientes.

## Consequências

- implantação e atualização centralizadas;
- porta da aplicação não exposta diretamente;
- SQLite limita a uma réplica;
- backups externos e restauração ensaiada são obrigatórios;
- alta disponibilidade exigirá nova decisão de persistência.
