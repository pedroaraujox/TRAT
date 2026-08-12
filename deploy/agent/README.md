# TRAT Agent — implantação Windows

O Agent é instalado nos servidores dos clientes e se comunica por HTTPS com `https://trat-hml.outboxtech.com.br` durante o MVP.

## Pacote oficial

O GitHub Actions da branch `development` gera `TRAT.Agent.Setup.exe` com a URL do MVP e o incorpora na imagem do ControlPlane. O operador deve baixá-lo pela página `/admin/downloads` para garantir que está usando a versão publicada.

## Conteúdo do pacote expandido

- `agent.settings.template.json`: modelo sem segredos;
- `Install-Agent.ps1`: instalação/atualização do serviço;
- `Update-Agent.ps1`: atualização in-place;
- `Uninstall-Agent.ps1`: remoção do serviço;
- `bin/`: serviço;
- `tray/`: status local;
- `installer/`: instalador GUI.

## Diretórios padrão

```text
Binários: C:\Program Files\TRAT\Agent
Estado: C:\ProgramData\TRAT\Agent
```

## Instalação segura

1. cadastre cliente e host no ControlPlane;
2. gere o token do cliente e mantenha-o em canal seguro;
3. baixe o Setup do ambiente correto;
4. execute como administrador;
5. confirme URL, cliente, host e caminhos;
6. valide prechecks e dry-run;
7. inicie o serviço;
8. confirme heartbeat no painel;
9. execute job pequeno antes do conjunto real;
10. valide objetos e status final no S3.

## Segredos

- o token do Agent é salvo via DPAPI LocalMachine;
- credenciais AWS podem ser protegidas por DPAPI;
- não grave tokens ou chaves em logs, documentos ou tickets;
- rotacione credenciais após suspeita de exposição;
- prefira credencial AWS exclusiva por cliente/prefixo.

## Atualização

Atualize in-place e preserve `C:\ProgramData\TRAT\Agent`. Depois confirme:

- serviço iniciado;
- versão reportada;
- heartbeat;
- política efetiva;
- dry-run;
- job real pequeno.

## Desinstalação

A remoção do serviço não autoriza apagar dados locais, manifests ou credenciais sem decisão explícita. Registre o motivo e preserve evidências quando houver incidente.

## Compatibilidade

Consulte [COMPATIBILIDADE-WINDOWS.md](../../docs/COMPATIBILIDADE-WINDOWS.md) e [AWS-IAM-MINIMO.md](../../docs/AWS-IAM-MINIMO.md).
