# Como Rodar o TRAT Localmente

Guia completo para configurar, rodar e testar o TRAT (Agent + ControlPlane) em uma máquina Windows de desenvolvimento. Este é o modo de operação atual do projeto — **não há publicação em VPS/nuvem no momento**, todo o ciclo de desenvolvimento e teste acontece localmente.

---

## 1. Visão Geral

O TRAT é composto por duas partes que rodam na mesma máquina (ou em máquinas diferentes na mesma rede) durante os testes:

- **ControlPlane**: painel web (ASP.NET Core) que você acessa pelo navegador em `http://localhost:5080`.
- **Agent**: serviço Windows que roda no host que será feito backup, instalado via `TRAT.Agent.Setup.exe` baixado do próprio painel.

Você pode testar tudo em uma única máquina (o painel e o "host cliente" sendo a mesma máquina) ou usar uma segunda máquina Windows na mesma rede pra simular um cliente real.

---

## 2. Pré-requisitos (o que precisa estar instalado)

### Obrigatório

| Ferramenta | Versão | Como verificar | Para quê |
|---|---|---|---|
| **.NET 8 SDK** | 8.0.x | `dotnet --version` | Compilar/rodar o ControlPlane, Tray e Installer |
| **.NET Framework 4.8 Developer Pack** | 4.8 | Painel de Controle → Programas (ou `dotnet build` do Service acusa erro se faltar) | Compilar o Agent Service (Windows Service legado) |
| **PowerShell 5.1+** | 5.1 (já vem no Windows) | `$PSVersionTable.PSVersion` | Rodar todos os scripts de build/publish (`Liberar-TRAT.ps1`, etc.) |
| **Windows 10/11 ou Windows Server** | — | — | O Agent só roda como Windows Service; o ControlPlane em modo produção também |
| **Git** | qualquer recente | `git --version` | Clonar/versionar o repositório |

### Opcional (mas recomendado)

| Ferramenta | Para quê |
|---|---|
| **Visual Studio 2022** ou **VS Code** | Editar/debugar o código com mais conforto |
| **Conta AWS + bucket S3** | Testar o upload real de backup (sem isso, dá pra testar tudo exceto o upload de fato). Veja [AWS-IAM-MINIMO.md](AWS-IAM-MINIMO.md) pra política mínima de permissões. |

### Instalar o .NET 8 SDK e o Developer Pack do .NET Framework 4.8

Baixe em: https://dotnet.microsoft.com/download/dotnet/8.0 (SDK, não só runtime)

Para o .NET Framework 4.8 Developer Pack: https://dotnet.microsoft.com/download/dotnet-framework/net48 (necessário só se for compilar o Agent Service; a maioria das distros do Windows já vem com o runtime, mas o **Developer Pack** — que traz as ferramentas de build — pode não vir por padrão).

---

## 3. Primeira Configuração (do zero)

### 3.1 Clonar o repositório

```powershell
git clone <url-do-seu-repositorio> TRAT
cd TRAT
```

### 3.2 Verificar as ferramentas

```powershell
dotnet --version        # deve mostrar 8.0.x
$PSVersionTable.PSVersion   # deve mostrar 5.1 ou superior
```

### 3.3 Rodar o fluxo completo de build (recomendado para a primeira vez)

```powershell
.\Liberar-TRAT.ps1
```

Esse comando único:
- Publica o Agent (Service + Tray + Installer) em `artifacts\agent-package`
- Publica o ControlPlane em `artifacts\trat-local\TRAT.ControlPlane.Local`, embutindo os artifacts do Agent (pra Download funcionar)
- Roda smoke tests (dry-run do Agent, login, checagens básicas de UI)
- Gera um relatório em `artifacts\release-reports`

Se demorar muito ou você quiser pular a parte de testes na primeira vez:

```powershell
.\Liberar-TRAT.ps1 -SkipSmokeTest -AllowIncompleteRelease
```

### 3.4 Configurar o admin inicial (opcional, mas recomendado)

Por padrão, o `appsettings.json` cria um admin com email `admin@trat.local` e senha literal `CHANGE_ME_ADMIN_PASSWORD` na primeira inicialização — **funciona, mas é melhor trocar** antes do primeiro start.

Copie o template e edite com seus dados:

```powershell
Copy-Item deploy\controlplane\appsettings.Local.template.json artifacts\trat-local\TRAT.ControlPlane.Local\appsettings.Local.json
notepad artifacts\trat-local\TRAT.ControlPlane.Local\appsettings.Local.json
```

Preencha:
```json
{
  "ControlPlane": {
    "Security": {
      "AdminToken": "qualquer-token-aleatorio-para-apis-internas"
    },
    "BootstrapAdmin": {
      "Email": "seu-email@exemplo.com",
      "DisplayName": "Seu Nome",
      "Password": "sua-senha-local-temporaria"
    }
  }
}
```

> A senha é usada **só na primeira inicialização** para criar sua conta — depois disso o próprio sistema apaga o campo `Password` do arquivo automaticamente (por segurança), então não precisa se preocupar em deixá-la lá depois.

### 3.5 Iniciar o painel

```powershell
.\Iniciar-TRAT.ps1
```

Isso abre automaticamente `http://localhost:5080` no navegador. Faça login com o email/senha configurados acima (ou o padrão, se pulou o passo 3.4).

---

## 4. Dia a Dia de Desenvolvimento

### 4.1 Ciclo básico ao alterar código

1. Edite o código em `src\Agent\*` ou `src\ControlPlane\*`
2. Rebuilde e republique **apenas o que mudou**:
   ```powershell
   # Mudou o Agent:
   scripts\agent\Publish-Agent.ps1

   # Mudou o ControlPlane:
   scripts\controlplane\Publish-ControlPlane.ps1

   # Mudou os dois:
   scripts\agent\Publish-Agent.ps1
   scripts\controlplane\Publish-ControlPlane.ps1
   ```
3. Reinicie o painel se ele já estava rodando:
   ```powershell
   .\Parar-TRAT.ps1
   .\Iniciar-TRAT.ps1
   ```
4. **Valide no artefato real** — nunca considere uma mudança pronta só porque compilou. Abra a tela de verdade, baixe o Setup.exe de verdade, rode o instalador de verdade. Veja o checklist completo em [CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md](CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md).

> **Importante**: `Publish-ControlPlane.ps1` preserva automaticamente `data/`, `logs/` e `appsettings.Local.json` entre republishes — seus clientes/hosts de teste cadastrados não somem a cada rebuild.

### 4.2 Compilar sem publicar (checagem rápida de sintaxe)

Quando você só quer saber se o C# compila, sem gerar pacote:

```powershell
dotnet build src\ControlPlane\ControlPlane.Api\ControlPlane.Api.csproj -c Release
dotnet build src\Agent\WebstationBackup.Agent.Service\WebstationBackup.Agent.Service.csproj -c Release
```

Isso **não substitui** o passo de publish acima — é só uma checagem intermediária mais rápida enquanto você escreve código.

### 4.3 Parar o painel

```powershell
.\Parar-TRAT.ps1
```

---

## 5. Testar o Fluxo Completo (ponta a ponta)

Depois que o painel estiver no ar (`http://localhost:5080`):

1. **Login** com o admin configurado.
2. **Criar um Cliente** (menu Clientes) — dê um ID sem espaço/acento (ex.: `cliente-teste`).
3. **Gerar o token do cliente** na tela do próprio cliente.
4. Ir em **Downloads** e baixar o `TRAT.Agent.Setup.exe`.
5. Rodar o Setup.exe (na mesma máquina ou em outra máquina Windows da rede, apontando o `ControlPlane URL` pro IP/porta correto se for outra máquina).
6. Preencher: **Token do cliente**, **AWS Access Key**, **AWS Secret Key**, e os **caminhos de backup**.
7. Clicar em **Instalar**.
8. No painel, ir em **Hosts** → **Gerenciar** o host recém-instalado → configurar **Bucket S3 / Região / Agenda**.
9. Rodar um **backup manual** e acompanhar o status no painel.
10. Conferir no S3 (console AWS) que os arquivos realmente subiram, e que rodar de novo **não reenvia** arquivos que já existem (deduplicação por SHA256).

---

## 6. Scripts de Referência

| Script | O que faz |
|---|---|
| `Liberar-TRAT.ps1` | Fluxo completo: publica Agent + ControlPlane, roda smoke test, gera relatório |
| `Iniciar-TRAT.ps1` | Sobe o painel local em `http://localhost:5080` |
| `Parar-TRAT.ps1` | Para o painel local |
| `Resetar-TRAT.ps1` | Limpa banco/config local, com backup de segurança antes |
| `Validar-TRAT.ps1` | Roda apenas os smoke tests (dry-run do Agent, login, heartbeat) |
| `scripts\agent\Publish-Agent.ps1` | Builda e empacota só o Agent (Service + Tray + Installer + Setup.exe) |
| `scripts\controlplane\Publish-ControlPlane.ps1` | Builda e empacota só o ControlPlane, embutindo os artifacts do Agent |

### Flags úteis do `Liberar-TRAT.ps1`

```powershell
.\Liberar-TRAT.ps1 -SkipAgentPublish                        # pula o Agent, atualiza só o ControlPlane
.\Liberar-TRAT.ps1 -SkipControlPlanePublish                  # pula o ControlPlane, atualiza só o Agent
.\Liberar-TRAT.ps1 -SkipSmokeTest -AllowIncompleteRelease    # publica sem validar (mais rápido, use com cautela)
.\Liberar-TRAT.ps1 -ResetLocalState                          # força reset do estado local antes de validar
```

### Flags úteis do `Iniciar-TRAT.ps1`

```powershell
.\Iniciar-TRAT.ps1 -RebuildLocalPackage    # força republish do ControlPlane antes de iniciar
.\Iniciar-TRAT.ps1 -ResetLocalState        # reseta banco/config antes de iniciar
.\Iniciar-TRAT.ps1 -NoBrowser              # não abre o navegador automaticamente
```

---

## 7. Resetar Tudo (voltar ao estado limpo)

Se quiser recomeçar do zero sem clientes/hosts/dados antigos:

```powershell
.\Resetar-TRAT.ps1
```

Isso faz backup de segurança do `controlplane.db` e do `appsettings.Local.json` antes de limpar — os backups ficam na própria pasta do pacote, com timestamp no nome.

---

## 8. Onde Ficam os Dados e Logs

| O quê | Onde |
|---|---|
| Banco de dados (SQLite) | `artifacts\trat-local\TRAT.ControlPlane.Local\data\controlplane.db` |
| Config local (git-ignored) | `artifacts\trat-local\TRAT.ControlPlane.Local\appsettings.Local.json` |
| Logs do painel | `artifacts\trat-local\TRAT.ControlPlane.Local\logs\*.log` |
| Pacote do Agent (Setup.exe/ZIP) | `artifacts\agent-package\` |
| Log do Agent no host instalado | `C:\ProgramData\TRAT\Agent\logs\agent.log` (varia conforme configurado no instalador) |
| Relatórios de release | `artifacts\release-reports\` |

---

## 9. Troubleshooting Comum

**Painel não abre / erro de porta em uso**
```powershell
Get-Process -Id (Get-NetTCPConnection -LocalPort 5080).OwningProcess
# Mate o processo se for uma instância antiga travada, ou rode .\Parar-TRAT.ps1
```

**Erro ao compilar o Agent Service (net48 não encontrado)**
Instale o .NET Framework 4.8 Developer Pack (não só o runtime) — veja seção 2.

**Instalador do Agent reclama de "CustomerId inválido"**
O ID do cliente/host só aceita letras (sem acento), números, ponto, hífen e underscore, sem espaço. Recrie o cliente no painel com um ID válido.

**Downloads mostra "nenhum pacote encontrado"**
Rode `scripts\agent\Publish-Agent.ps1` e depois `scripts\controlplane\Publish-ControlPlane.ps1` (o ControlPlane precisa reembutir os artifacts do Agent após cada publish do Agent).

**Perdi meus clientes/hosts de teste depois de um rebuild**
Não deveria acontecer — `Publish-ControlPlane.ps1` preserva `data/`, `logs/` e `appsettings.Local.json` automaticamente. Se aconteceu, verifique se rodou com `-ResetLocalState` sem querer.

**Erro de login (senha não funciona)**
Se você já tinha um admin criado e mudou a senha em `appsettings.Local.json`, isso **não** atualiza a senha de uma conta já existente (o bootstrap só cria a conta na primeira vez). Use `Resetar-TRAT.ps1` se precisar recomeçar, ou troque a senha pela própria tela do painel (se existir essa opção) ou diretamente no banco.

---

## 10. Próximos Passos

Depois de validar tudo localmente, os próximos marcos de desenvolvimento (fora do escopo deste guia) são:
- Continuar a evolução do MVP com testes locais
- Quando decidirmos publicar (VPS/nuvem), revisitaremos a documentação de deployment nessa ocasião

Para regras de desenvolvimento/liberação (o que precisa ser validado antes de considerar algo "pronto"), veja [CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md](CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md).
