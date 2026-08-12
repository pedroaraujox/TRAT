# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**TRAT** is a Windows-only backup orchestration system: a Windows Service (**Agent**) runs on client machines, scans configured paths, uploads to AWS S3 with SHA256-based deduplication, and reports status to a central web panel (**ControlPlane**). Backups are immutable — the Agent can never delete from S3.

**Current phase: central homologation preparation.** The target architecture is one internet-accessible ControlPlane operated by Outbox Tech plus Agents on customer servers. Local mode remains for development only. The homologation hostname is `https://trat-hml.outboxtech.com.br`; see `docs/ARQUITETURA-CENTRAL-E-HOMOLOGACAO.md`. SQLite remains the only supported database for the pilot.

The project's own docs, scripts, and commit messages are in Portuguese; code and identifiers are in English.

---

## Commands

### Build & publish (must run after any code change — compiling alone is not validation)

```powershell
.\Liberar-TRAT.ps1                          # full flow: publish Agent + ControlPlane, smoke test, release report
.\Liberar-TRAT.ps1 -SkipAgentPublish        # only republish ControlPlane
.\Liberar-TRAT.ps1 -SkipControlPlanePublish # only republish Agent
.\Liberar-TRAT.ps1 -SkipSmokeTest -AllowIncompleteRelease  # publish without validating (fast iteration)
.\Liberar-TRAT.ps1 -ResetLocalState         # wipe local db/config before validating

scripts\agent\Publish-Agent.ps1             # just the Agent (Service -> Tray -> Installer, outputs Setup.exe + ZIP)
scripts\controlplane\Publish-ControlPlane.ps1  # just the ControlPlane (embeds Agent artifacts for the Downloads page)
```

`Publish-ControlPlane.ps1` auto-backs up and restores `data/`, `logs/`, and `appsettings.Local.json` around the republish — test customers/hosts survive rebuilds by design. Don't reset local state unless the change actually requires it.

### Run / stop the local panel

```powershell
.\Iniciar-TRAT.ps1          # starts http://localhost:5080 (installs as Windows Service if run elevated, else runs as a process)
.\Parar-TRAT.ps1
.\Resetar-TRAT.ps1          # wipe db/config, with a safety backup first
.\Validar-TRAT.ps1 -AdminEmail <email> -AdminPassword <password>   # smoke test only: login, dashboard, downloads
```

### Tests

```powershell
dotnet test tests\ControlPlane.Api.Tests\ControlPlane.Api.Tests.csproj -c Release
dotnet test tests\ControlPlane.Api.Tests\ControlPlane.Api.Tests.csproj --filter "FullyQualifiedName~CreateBootstrapPolicy_CreatesAndAssigns_WhenHostIsReady"  # single test
```
xUnit. Only the ControlPlane API has automated tests today — nothing covers the Agent.

### Quick compile check (not a substitute for publish)

```powershell
dotnet build src\ControlPlane\ControlPlane.Api\ControlPlane.Api.csproj -c Release
dotnet build src\Agent\WebstationBackup.Agent.Service\WebstationBackup.Agent.Service.csproj -c Release
```

---

## Architecture

### Two runtimes, one repo

- **ControlPlane.Api** (`src/ControlPlane/ControlPlane.Api`, .NET 8, ASP.NET Core MVC): the web panel + REST API. Published self-contained single-file (`--self-contained true -p:PublishSingleFile=true`) — the target machine needs no .NET install at all. SQLite only, EF Core with **no migrations framework**: schema is created via `EnsureCreatedAsync()` and then patched idempotently by `Data/SchemaBootstrapper.cs` (`ALTER TABLE ... ADD COLUMN`, wrapped so re-running against an already-patched db is a harmless no-op).
- **Agent** (`src/Agent`, three projects): `WebstationBackup.Agent.Service` (.NET Framework 4.8, the actual Windows Service doing the backup work — internal modules under `Aws/`, `Backup/`, `Compatibility/`, `Core/`, `Logging/`, `Security/`, `Service/`, `Telemetry/`), `WebstationBackup.Agent.Tray` (.NET 8 WPF status UI), `WebstationBackup.Agent.Installer` (.NET 8 WinForms onboarding GUI). `TRAT.Agent.Setup.exe` is a standalone installer: it embeds the whole package as a resource and self-extracts to `%LOCALAPPDATA%\TRAT\AgentSetupPayload` if the sibling files aren't present, so distributing one .exe is enough.

### One middleware gates four different auth models

`Security/ApiTokenAuthMiddleware.cs` is the single chokepoint for all request auth, branching by path prefix:
- `/api/v1/health` — open, no auth.
- `/api/v1/agents/*` (`AgentIngestController`) — per-customer token via `X-Agent-Token` header. The token is **not** the `ControlPlane:Security:AgentToken` appsettings value (that key is unused/dead config left over from an earlier design) — it's a per-customer secret generated/regenerated in `HomeController`, SHA256-hashed, and checked against `Customer.AgentEnrollmentTokenHash`.
- `/api/v1/admin/*` (`AdminController`, machine-readable admin API) — single global token via `X-Admin-Token`, compared against `ControlPlane:Security:AdminToken` with a constant-time check.
- `/admin/*` (`HomeController` + `PanelAdminController`, the human panel UI) — cookie session (`PanelSecurityConstants.SessionUserId`); `/admin/panel/*` additionally requires the admin role.

### Agent <-> ControlPlane contract (`AgentIngestController`, base route `api/v1/agents`)

`enroll` -> `bootstrap/paths` -> `effective-policy` / `run-request/next` -> `heartbeat` -> `jobs/start` -> `jobs/progress` -> `jobs/final` -> `configuration/report`. Any change to this contract needs end-to-end validation (enroll, sync, heartbeat, a real or scheduled run, final status in the panel) — see `docs/CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md`.

Job lifecycle states (`project.rules.json`): `QUEUED -> PRECHECK -> SCANNING -> UPLOADING -> VERIFYING -> FINALIZING -> SUCCEEDED|FAILED|CANCELED`. A job only counts as `SUCCEEDED` if bytes/count planned equal bytes/count confirmed on S3.

### Domain model (`Domain/Models.cs`)

`Customer`, `Host`, `Job`, `Artifact`, `Alert`, `AuditEvent`, `BackupPolicy`, `AgentRunRequest`, `PolicyChangeEvent`, `AgentConfiguration`, `PanelUser`. `PolicyResolutionService` resolves the effective policy for a host (host-level override vs. customer-level default).

### Copying the central homologation package to its server

The primary central deployment uses separate `trat-hml` and `trat-prod` stacks on the Contabo Portainer, connected only to the existing Nginx Proxy Manager network. Do not install a ControlPlane per customer, publish port 8080 on the host, share volumes between environments, or run more than one SQLite replica. The Windows/Cloudflare package remains an alternative deployment path.

---

## Non-negotiable operational rules

From `project.rules.json` and `docs/CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md`:

- **S3 is append-only**: Agent's allowed S3 operations are `PutObject`/multipart upload/`ListBucket`/`GetBucketLocation`/`HeadObject` only — never delete. The bucket must have versioning, Object Lock, and blocked public access. Don't add delete capability, even for a "cleanup" feature, without explicit discussion.
- **Rebuild/republish, then validate the artifact, not the source.** A change isn't done because `dotnet build` succeeded — it's done when the packaged/published artifact was actually exercised (real download, real install, real service start, real run).
- **Preserve test state.** Prefer updating an already-installed Agent/ControlPlane in place over reinstalling or recreating customers/hosts. Only reset when the change is to the install/onboarding flow itself.
- **PowerShell 5.1 only** in every script (no PS 6+/Core syntax) — target is Windows Server 2012+.
- File restore through the product and ControlPlane high availability remain out of scope. Internet-facing central homologation is now in scope and must follow the hardening/deployment guide.

---

## Key docs

- `docs/COMO-RODAR-LOCALMENTE.md` — local development and troubleshooting only.
- `docs/ARQUITETURA-CENTRAL-E-HOMOLOGACAO.md` — central topology, deployment and public validation.
- `docs/COMPATIBILIDADE-WINDOWS.md` — separate ControlPlane and Agent support matrices.
- `docs/CHECKLIST-DESENVOLVIMENTO-E-LIBERACAO.md` — the validation rules summarized above, in full.
- `docs/MVP-1-MES-PRONTIDAO.md` — MVP readiness gate for a 1-month pilot; what's explicitly out of scope.
- `docs/AWS-IAM-MINIMO.md` — minimal IAM policy for the S3 bucket the Agent writes to.
- `project.rules.json` — machine-readable source of truth for job states, S3 safety rules, and default backup policy (schedule, throttling, retry, alerts).
