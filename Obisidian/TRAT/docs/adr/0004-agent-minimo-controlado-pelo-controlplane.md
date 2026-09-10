# ADR-0004 — Agent mínimo controlado pelo ControlPlane

- Data: 2026-09-10
- Estado: aceita

## Contexto

O instalador permitia informar ou reaproveitar URL, cliente, host, pastas, exclusões e outros valores locais. Isso duplicava a configuração operacional do ControlPlane, dificultava saber qual fonte estava efetiva e permitia que uma reinstalação preservasse parâmetros antigos.

## Decisão

Durante o MVP, o usuário informa no Agent somente:

1. token de comunicação/enrollment do ControlPlane;
2. AWS Access Key;
3. AWS Secret Key.

A URL do ControlPlane é incorporada ao pacote de cada ambiente. Cliente e host são obtidos durante o enrollment usando o token e a identidade da máquina. Pastas, exclusões, bucket, região, prefixo, agenda e limites são definidos exclusivamente no ControlPlane.

O token e as credenciais AWS permanecem protegidos localmente por DPAPI. Eles nunca são enviados ao ControlPlane, exceto o token no cabeçalho autenticado das chamadas do Agent. Chaves AWS não podem aparecer em logs, relatórios ou no painel.

Cada pacote deve carregar revisão e versão identificáveis. A mesma revisão deve aparecer no instalador, nos metadados do Windows, no Tray e no relatório enviado ao ControlPlane.

## Consequências

- o Agent não executa backup sem política completa recebida do ControlPlane;
- valores operacionais antigos presentes em `agent.settings.json` são ignorados;
- cada ambiente exige seu pacote correspondente;
- reinstalação exige novamente as três informações sensíveis;
- mudanças operacionais deixam de exigir acesso ao servidor do cliente;
- homologação precisa validar o executável realmente distribuído, não apenas o código-fonte.

