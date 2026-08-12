# Arquitetura central e homologacao

## Decisao de arquitetura

O TRAT utiliza um unico ControlPlane central. Os servidores dos clientes recebem somente o Agent.

```text
Operadores Outbox Tech -> HTTPS -> ControlPlane central
                                      ^
                                      |
                    HTTPS + token por cliente
                                      |
             Agents instalados nos servidores dos clientes
                                      |
                                      v
                              buckets AWS S3
```

O cadastro `Customer` representa um cliente dentro da instalacao central. Nao deve ser criada uma instalacao independente do ControlPlane para cada cliente.

## Homologacao

- hostname: `trat-hml.outboxtech.com.br`;
- origem: container `trat-hml:8080`, acessivel somente pela rede Docker do proxy;
- borda HTTPS: Nginx Proxy Manager na VPS Contabo;
- banco: SQLite persistente em volume Docker exclusivo da stack;
- backup: copia SQLite consistente em outro volume exclusivo da stack;
- producao: stack separada `trat-prod`, ativada apenas depois do aceite do piloto.

Nenhuma porta do container e publicada no host. Somente o proxy reverso compartilha a rede Docker externa `nginx-proxy_default` com o ControlPlane.

## Publicar pela Contabo e Portainer

O procedimento principal esta em `deploy\portainer\README.md`. A imagem e gerada pela automacao `.github\workflows\publish-controlplane-container.yml`; o Agent Windows e compilado em runner Windows e incorporado na imagem Linux do painel.

SQLite exige uma unica replica. Homologacao e producao devem ter bancos, volumes, credenciais e tokens totalmente separados.

## Alternativa Windows

## Preparar a pasta transferivel

Na maquina de desenvolvimento:

```powershell
scripts\controlplane\Publish-Homologacao-Central.ps1
```

Transfira somente:

```text
artifacts\trat-homologacao\TRAT.ControlPlane.Homologacao.zip
```

### Configurar o Cloudflare

No painel Cloudflare:

1. crie um Tunnel gerenciado para a homologacao;
2. crie uma aplicacao publicada com hostname `trat-hml.outboxtech.com.br`;
3. use como Service URL `http://127.0.0.1:5080`;
4. copie o token de instalacao do conector Windows;
5. nao reutilize esse token em scripts, documentos ou commits.

### Instalar no servidor central

Recomendacao: Windows Server 2022 x64 atualizado.

1. extraia o ZIP em uma pasta local nao sincronizada, por exemplo `C:\TRAT\ControlPlane`;
2. abra PowerShell como Administrador;
3. execute:

```powershell
.\Instalar-Homologacao-Central.ps1
```

O script solicita a conta administrativa inicial e o token do Tunnel, instala os dois servicos, valida o health local e agenda o backup diario.

## Validar externamente

```powershell
.\Validar-Homologacao-Central.ps1 -PublicUrl "https://trat-hml.outboxtech.com.br"
```

Depois da validacao automatica:

1. entre no painel;
2. crie um cliente piloto;
3. gere o token do cliente;
4. baixe o Setup do Agent na pagina Downloads;
5. instale em um host piloto;
6. confirme enroll, heartbeat, configuracao, job completo e objetos no S3;
7. desligue a internet do host por alguns minutos e valide a retomada;
8. reinicie o servidor central e confirme o retorno dos dois servicos.

## Seguranca aplicada

- HTTPS obrigatorio no acesso publico;
- Kestrel vinculado apenas ao loopback;
- forwarded headers aceitos somente de proxies explicitamente confiaveis;
- cookies `Secure`, `HttpOnly`, `SameSite=Strict` e prefixo `__Host-`;
- HSTS, CSP e outros cabecalhos defensivos;
- limites de requisicoes diferenciados para login, Agents e API;
- bloqueio temporario de conta apos falhas de login;
- antiforgery nos formularios administrativos;
- API `/api/v1/admin` desativada por padrao;
- token de Agent separado por cliente e armazenado apenas como hash no painel;
- SQLite, chaves Data Protection e configuracao incluidos no backup operacional.

## Limites da homologacao

SQLite continua adequado para o piloto controlado com uma unica instancia do ControlPlane. Nao se deve executar duas instancias simultaneas apontando para o mesmo arquivo. Antes de escala maior ou alta disponibilidade, deve ser reavaliada a persistencia.
