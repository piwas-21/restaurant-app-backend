# RUMI Backend

Restaurant management system backend — REST API, CQRS, EF Core, PostgreSQL.

> **For agents:** read [CLAUDE.md](CLAUDE.md) first; it auto-loads in Claude Code sessions and lists the rules every change must follow.

---

## Stack

- **.NET 10** (Web API)
- **EF Core 10** + PostgreSQL
- **Custom CQRS mediator** ([CustomMediator.cs](RestaurantSystem.Api/Common/CustomMediator.cs)) — NOT MediatR. See [ADR-001](docs/adr/ADR-001-custom-cqrs-mediator.md).
- **JWT Bearer auth** with role-based authorization (Customer / Cashier / Admin). See [ADR-003](docs/adr/ADR-003-jwt-scope-and-claims.md).
- **Soft-delete** via global query filter. See [ADR-002](docs/adr/ADR-002-soft-delete-strategy.md).
- **xUnit** integration tests
- Hosted on GitLab; CI runs gitleaks, GitLab SAST, and Trivy image scan per `.gitlab-ci.yml`.

## Repository layout

```
RestaurantSystem.Api/             # Controllers, CQRS handlers, Features
├── Abstraction/Messaging/        # ICommand, IQuery, IHandler interfaces
├── BackgroundServices/           # Cleanup workers (data-loss class — handle with care)
├── Common/                       # Shared infra: CustomMediator, exceptions, services
├── Features/                     # One folder per feature (Orders, Reservations, etc.)
└── Settings/                     # IOptions<T> POCOs (Email, Jwt, Cors, ...)
RestaurantSystem.Domain/          # Pure domain entities + enums (no EF, no ASP.NET)
RestaurantSystem.Infrastructure/  # EF Core, persistence, migrations
RestaurantSystem.IntegrationTests/  # xUnit integration tests
docs/                             # SPRINT-PLAN, ADRs, security audit, dev guidelines
scripts/                          # Local dev orchestration
.gitlab/                          # MR templates, CI templates
```

## Quick start (new clone)

```bash
# 1. Install pre-commit hooks (one-time)
bash scripts/setup_hooks.sh

# 2. Bootstrap local secrets file (one-time)
bash scripts/dev-secrets.sh
# then edit RestaurantSystem.Api/app-secrets.json with real local values

# 3. Bring up the dev stack (postgres + redis + api)
bash scripts/dev-up.sh

# Tear down
bash scripts/dev-down.sh
```

`dev-up.sh --reset` nukes the postgres data volume for a clean slate. `dev-up.sh --no-run` brings up the DB without starting the API (useful when you want to attach a debugger from your IDE).

## Branch strategy

```
main          ← production deployment (currently develop; cutover pending)
└── develop   ← test environment (auto-deployed)
     ├── feature/<x>
     ├── fix/<x>
     ├── chore/<x>
     └── docs/<x>
```

Pre-commit hook blocks direct commits to `main` and `develop`. Branch off `develop`, open MR to `develop` using the [default MR template](.gitlab/merge_request_templates/Default.md). After test-env validation, `develop` is promoted to `main` for production.

### Branch protection (GitLab)

Configured in **Settings → Repository → Protected Branches**:

| Branch | Allowed to push | Allowed to merge | Force push |
|---|---|---|---|
| `main` | No one | Maintainers | Disabled |
| `develop` | No one | Maintainers + Developers | Disabled |

All MRs require the pipeline to pass before merge.

## Configuration

| Setting | Source | Notes |
|---|---|---|
| Connection strings | `app-secrets.json` (gitignored) | `dev-secrets.sh` bootstraps from template |
| `EmailSettings` | `app-secrets.json` + `appsettings.<Env>.json` | `FrontendBaseUrl`, `BackendBaseUrl` are `[Required] [Url]` — no localhost defaults |
| `JwtSettings` | `app-secrets.json` | Secret must be ≥ 32 bytes |
| `CorsSettings:AllowedOrigins` | `appsettings.<Env>.json` | App throws on startup in non-Dev if empty |

## Pull requests

Every MR uses [.gitlab/merge_request_templates/Default.md](.gitlab/merge_request_templates/Default.md). It auto-loads when you create an MR via the GitLab UI or `glab mr create`.

Required sections: summary, sprint-task link, acceptance-criteria coverage, schema/contract verification (for DB/DTO changes), standard checklist, test plan, deploy notes.

## Documentation

| File | Purpose |
|---|---|
| [CLAUDE.md](CLAUDE.md) | Agent rules — auto-loaded |
| [docs/SPRINT-PLAN.md](docs/SPRINT-PLAN.md) | 8-sprint refactoring plan |
| [docs/DEVELOPMENT-GUIDELINES.md](docs/DEVELOPMENT-GUIDELINES.md) | Coding conventions |
| [docs/SECURITY-AUDIT.md](docs/SECURITY-AUDIT.md) | Security findings + status |
| [docs/TEST-COVERAGE-PLAN.md](docs/TEST-COVERAGE-PLAN.md) | Test strategy |
| [docs/QUALITY-SECURITY-PLAN.md](docs/QUALITY-SECURITY-PLAN.md) | CI / quality / security gate plan |
| [docs/adr/](docs/adr/) | Architecture Decision Records |
