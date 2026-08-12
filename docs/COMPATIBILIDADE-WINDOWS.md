# Compatibilidade com Windows Server

ControlPlane e Agent possuem matrizes diferentes. Compatibilidade com Windows antigo deve ser resolvida no Agent, nao levando o ControlPlane para cada servidor.

## ControlPlane central

| Sistema | Nivel | Observacao |
|---|---|---|
| Windows Server 2022 x64 | recomendado | plataforma da homologacao |
| Windows Server 2025 x64 | suportado | validar antes de promover |
| Windows Server 2019 x64 | suportado | alternativa aceitavel |
| Windows Server 2016 x64 | compativel | nao recomendado para nova hospedagem |
| 2012/2012 R2 e anteriores | nao usar como ControlPlane central | manter o servidor central em SO moderno |

## Agent

| Sistema | Nivel planejado | Requisitos e limites |
|---|---|---|
| Windows Server 2025/2022/2019/2016 x64 | suporte principal | Setup, Service e Tray |
| Windows Server 2012 R2 x64 | suporte de piloto | .NET Framework 4.8, TLS 1.2, atualizacoes do Windows e teste real obrigatorios |
| Windows Server 2012 x64 | suporte de piloto | mesmos requisitos; validar por maquina |
| Windows Server 2008 R2 SP1 x64 | melhor esforco | o Service net48 pode ser viavel, mas Setup/Tray modernos nao sao prometidos; pode exigir instalacao manual |
| Windows Server 2008 sem R2 | fora do suporte padrao | limitacoes de runtime, TLS e ciclo de vida; tratar como projeto excepcional |

## Regras

- comunicacao remota com o ControlPlane exige HTTPS e TLS 1.2;
- HTTP e aceito somente para localhost em desenvolvimento;
- o instalador e publicado com a URL central de homologacao;
- cada versao de Windows do piloto deve passar por: instalacao, reboot, heartbeat, sync, job real, retomada de rede e update in-place;
- nao declarar suporte baseado apenas em compilacao;
- o Agent nunca recebe permissao de exclusao no S3.

## Navegador

O painel central deve ser operado em navegador moderno e atualizado. Internet Explorer nao faz parte do suporte do painel. Os servidores clientes nao precisam abrir o painel para o Agent funcionar; o download do instalador pode ser feito em uma estacao administrativa e transferido ao servidor.
