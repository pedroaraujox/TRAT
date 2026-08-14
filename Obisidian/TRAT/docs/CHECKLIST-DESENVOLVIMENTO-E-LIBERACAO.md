# Checklist de desenvolvimento e liberação

## Regra principal

Uma mudança só está pronta quando o artefato realmente distribuído foi validado. Compilar o código-fonte não é evidência suficiente.

## Antes de alterar

- confirmar que a branch é `development`;
- executar `git status` e preservar mudanças não relacionadas;
- identificar banco, volumes e segredos afetados;
- definir como validar e como retornar à versão anterior;
- não trabalhar diretamente em `production`.

## Validação por área

### ControlPlane

- executar testes automatizados;
- compilar em Release;
- construir a imagem Docker;
- validar `/api/v1/health`, login e tela alterada;
- confirmar persistência após restart quando houver impacto em dados;
- revisar logs sem expor segredos.

### Agent

- executar `scripts\agent\Publish-Agent.ps1`;
- validar o `TRAT.Agent.Setup.exe` final;
- baixar novamente pela página `/admin/downloads`;
- testar instalação ou atualização in-place;
- validar dry-run, serviço, heartbeat e logs.

### Integração

- cadastrar/configurar cliente e host;
- validar enroll, sync e heartbeat;
- executar job manual ou agendado;
- confirmar status final e objetos no S3;
- testar retomada de rede quando aplicável.

### Infraestrutura

- executar `docker compose config`;
- construir e iniciar localmente;
- manter porta 8080 não publicada na VM;
- manter uma única réplica SQLite;
- atualizar com **Pull and redeploy**, nunca recriando volumes;
- confirmar container `healthy` e HTTPS público.

## Antes do push

```powershell
git diff --check
dotnet test tests\ControlPlane.Api.Tests\ControlPlane.Api.Tests.csproj -c Release
git status
```

Execute build e testes em sequência quando ambos usam as mesmas pastas `obj`; execução paralela pode gerar falso erro de arquivo bloqueado.

Confirme que não existem no diff:

- `.env`;
- bancos SQLite;
- logs;
- tokens ou senhas;
- ZIPs, executáveis e relatórios gerados.

## Depois do push em development

1. aguardar o workflow **Publicar ControlPlane**;
2. confirmar publicação da tag `:development`;
3. atualizar `trat-dev` no Portainer;
4. aguardar `healthy`;
5. testar health, login e fluxo impactado;
6. registrar evidência e eventual rollback.

## Gates de saída

Não liberar quando:

- testes falham;
- imagem ou instalador está desatualizado;
- validação ocorreu apenas no código-fonte;
- há risco não mitigado de perda de dados;
- rollback não está definido para mudança relevante;
- healthcheck permanece `unhealthy`;
- logs contêm dados sensíveis;
- revisão implantada não corresponde ao SHA esperado.

## Produção

Depois de todos os gates de homologação:

1. registrar o SHA aprovado e as evidências;
2. confirmar backup e restauração testável;
3. abrir e revisar o merge `development -> production`;
4. aguardar a publicação de `:production` e `:<SHA>`;
5. fazer backup antes do redeploy de `trat-prod`;
6. redeploy sem remover volumes;
7. validar ambiente, revisão, health, login, download do Agent, heartbeat e job real;
8. executar rollback imediato se qualquer gate falhar.
