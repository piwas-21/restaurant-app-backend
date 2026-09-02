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
docs/                             # ADRs, API contracts, security audit, dev guidelines
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
| `SENTRY_DSN` (+ optional `SENTRY_ENVIRONMENT`) | env var (box `.env` → compose passthrough, deploy repo) | Empty/unset = Sentry never initializes (inert). Errors only — no PII, no request bodies, tracing off. Enable runbook: deploy repo `DEPLOYMENT.md` §Error tracking |
| `ReservationQuickActions` | `appsettings.json` + env override | Signs the approve/reject links in the restaurant's alert mail. See below |

### Reservation quick-action links

`GET /api/reservations/{id}/quick-approve` and `.../quick-reject` are opened from the restaurant's
alert mail, so they carry no session and stay `[AllowAnonymous]`. What authorises them is a `?token=`
the mail puts on the link: an HMAC-SHA256 over the reservation id, the action, and the booking's
**current status**, with an expiry (backend #402). Signing the status is what makes a link one-shot —
once the booking is approved or rejected, both buttons in that mail stop working.

Before this, the bare reservation id was the whole authorisation, and `POST /api/Reservations` is
anonymous and returns that id to whoever made the booking. A guest could approve their own table.

| Key | Default | Meaning |
|---|---|---|
| `ReservationQuickActions:SigningKey` | `""` | HMAC key material. **Leave empty on an existing box:** the key is then derived (HKDF, purpose-labelled) from the already-required `JwtSettings:Secret`, so no new environment variable is needed to deploy. Set it to rotate link signatures independently of the JWT secret — every link already in an inbox stops working when you do |
| `ReservationQuickActions:LinkLifetimeDays` | `7` | How long a freshly minted link is valid. After that the restaurant decides in the dashboard |
| `ReservationQuickActions:LegacyLinkGraceDays` | `14` | Migration window for alert mails sent **before** signing shipped, which carry no token. Measured from each reservation's own `CreatedAt`, so it closes booking by booking. Every legacy use is logged at **warning** level |
| `ReservationQuickActions:LegacyLinkCutoffUtc` | unset | Optional instant; a reservation created at or after it can never take the token-less path |

**Closing the migration window.** The grace window is anchored per booking, so it needs no follow-up:
once every reservation created before the release is older than `LegacyLinkGraceDays`, nothing can use
a token-less link again. Two ways to close it sooner, both config-only — no code change, no redeploy of
new code:

1. **Recommended, at release:** set `ReservationQuickActions__LegacyLinkCutoffUtc` to the release
   timestamp (e.g. `2026-08-24T00:00:00Z`). Mails already sent keep working; bookings made after the
   release are signed-only from the first minute. Without it, the window also covers **new** bookings,
   which leaves #402 open for its length.
2. **Once the inbox is drained:** set `ReservationQuickActions__LegacyLinkGraceDays=0`. Every
   token-less link then lands on the "this link can no longer be used" page, which links to the
   reservations dashboard.

Watch for `Accepted a LEGACY unsigned quick-…` warnings in the box logs to tell when the old mails have
stopped being used.

## Pull requests

Every MR uses [.gitlab/merge_request_templates/Default.md](.gitlab/merge_request_templates/Default.md). It auto-loads when you create an MR via the GitLab UI or `glab mr create`.

Required sections: summary, sprint-task link, acceptance-criteria coverage, schema/contract verification (for DB/DTO changes), standard checklist, test plan, deploy notes.

## CI gates

The gates are the workflows, not a plan doc: [.github/workflows/ci.yml](.github/workflows/ci.yml) (dotnet format,
file-length, `dotnet build` warnings-as-errors, sharded integration tests + merged-coverage floors, gitleaks,
TruffleHog, Trivy fs, license compliance, OSV-Scanner),
[.github/workflows/security-audit.yml](.github/workflows/security-audit.yml) (weekly full-tree OSV + Trivy fs +
NuGet vulnerability audit + license compliance), [.pre-commit-config.yaml](.pre-commit-config.yaml) (the same
format / file-length / build gates locally) and SonarCloud automatic analysis (quality gate enforced by the
workspace merge gate). Cross-repo gate status lives in the workspace
[DEV-PHASES-PLAN.md](../docs/plans/DEV-PHASES-PLAN.md) §2 — that table is authoritative.

**Planned but never built** (carried over from the retired GitLab-era hardening plan, still open):

- **Container image scan.** [build-image.yml](.github/workflows/build-image.yml) builds and pushes to GHCR with no
  `trivy image` step. Trivy fs scans the source tree only, so OS/base-image CVEs in the published image are unscanned.
- **SBOM artifact.** No SPDX SBOM is produced for a release image (`Microsoft.Sbom.Tool` / Component Detection was
  the plan). Nothing consumes one today, which is why it stayed unbuilt.

Deliberately dropped from that plan: per-PR affected-test selection (measured and rejected — sharding is faster),
a hand-written `gitleaks.toml` (default config + `.secrets.baseline` cover it), `sonar-project.properties`
(SonarCloud autoscan), and the flat "≥70% coverage" gate (superseded by the measured per-metric floors in
`scripts/merge-coverage.py`).

## Documentation

| File | Purpose |
|---|---|
| [CLAUDE.md](CLAUDE.md) | Agent rules — auto-loaded |
| [docs/DEVELOPMENT-GUIDELINES.md](docs/DEVELOPMENT-GUIDELINES.md) | Coding conventions |
| [docs/SECURITY-AUDIT.md](docs/SECURITY-AUDIT.md) | Security findings + status |
| [docs/api/mobile-client-contracts.md](docs/api/mobile-client-contracts.md) | API contracts for mobile/web clients: reservation self-update, `has-password`/`set-password`, Apple sign-in, swagger schema ids |
| [docs/adr/](docs/adr/) | Architecture Decision Records |
