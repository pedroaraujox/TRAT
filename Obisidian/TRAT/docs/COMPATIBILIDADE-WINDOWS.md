# Compatibilidade de plataformas

## ControlPlane

O ControlPlane remoto oficial do MVP roda em container Linux na Contabo. Não depende do sistema operacional dos clientes.

| Plataforma | Estado |
| --- | --- |
| Docker Linux x64 | suporte principal do MVP |
| Docker Desktop + WSL 2 | desenvolvimento local |
| Windows Server 2019/2022/2025 | execução nativa alternativa |
| Windows Server 2016 | compatível, não recomendado para nova hospedagem |
| Windows Server 2012/R2 | não usar como ControlPlane central |

## Agent Windows

| Sistema | Nível | Condições |
| --- | --- | --- |
| Windows Server 2016/2019/2022/2025 x64 | suporte principal | teste real de instalação, reboot e job |
| Windows Server 2012/R2 x64 | piloto condicionado | .NET Framework 4.8, TLS 1.2 e patches atuais |
| Windows Server 2008 R2 SP1 | melhor esforço | pode exigir instalação manual |
| Windows Server 2008 sem R2 | fora do suporte | limitações de runtime e TLS |

## Matriz mínima de validação do Agent

Cada versão declarada como suportada deve comprovar:

- instalação;
- inicialização automática após reboot;
- TLS 1.2 até o ControlPlane;
- enroll e heartbeat;
- sincronização de política;
- job real e integridade no S3;
- retomada após perda de rede;
- atualização in-place.

## Navegadores

O painel suporta versões atuais de Chrome, Edge e Firefox. Internet Explorer não é suportado.
