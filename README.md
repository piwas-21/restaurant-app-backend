# RUMI Backend

Restaurant management system backend — REST API, CQRS, EF Core, PostgreSQL.

> **For agents:** read [CLAUDE.md](CLAUDE.md) first; it auto-loads in Claude Code sessions and lists the rules every change must follow.

---

## Stack

- **.NET 10** (Web API)
- **EF Core 10** + PostgreSQL
- **Custom CQRS mediator** ([CustomMediator.cs](RestaurantSystem.Api/Common/CustomMediator.cs)) — NOT MediatR. See [ADR-001](docs/adr/ADR-001-custom-cqrs-mediator.md).
- **Cashier printing:** Durable dashboard bills use the same current-ticket projection as staff/browser receipts, including committed amendments and related rounds. Structured ingredient selections/removals remain available to printer apps; free-text kitchen notes stay off cashier bills. Printer app 1.0.41 exposes the optional ingredient/sauce display switch (off by default).
- **JWT Bearer auth** with role-based authorization (Customer / Server / Cashier / Admin). See [ADR-003](docs/adr/ADR-003-jwt-scope-and-claims.md).
- **Soft-delete** via global query filter. See [ADR-002](docs/adr/ADR-002-soft-delete-strategy.md).
- **xUnit** integration tests
- Hosted on GitHub; CI runs build, format, tests, dependency/license checks, secret scans and Trivy filesystem checks through [.github/workflows/ci.yml](.github/workflows/ci.yml).

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
.github/                          # Pull-request template and CI/deploy workflows
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
main          ← production releases only
└── develop   ← default integration branch; staging deployment
     ├── feature/<x>
     ├── fix/<x>
     ├── chore/<x>
     └── docs/<x>
```

Pre-commit hooks block direct commits to `main` and `develop`. Branch off `develop` and open a pull request to `develop` using the [pull-request template](.github/pull_request_template.md). Production releases use a separate `develop` → `main` pull request; merging that release triggers deployment.

### Branch protection and merge gates

Follow [CLAUDE.md §8](CLAUDE.md#8--git-workflow) for the protected-branch requirements. Direct pushes and protection bypasses are prohibited; pull requests require green checks and resolved review threads.

Merge through the workspace `scripts/pr-merge-gate.sh piwas-21/restaurant-app-backend <pr> --merge`. In addition to CI and review threads, it requires the authenticated Sonar PR quality gate and zero open Sonar issues. A merge to `develop` does not activate default-off tenant features or establish production acceptance.

### After a production release

Bring the production merge commit back into `develop` through a reviewed PR before the next
release. Matching file contents alone do not satisfy strict branch freshness: `main` must be an
ancestor of the release branch.

1. Fetch `origin/main` and `origin/develop`, then create a `chore/` branch from `origin/develop`
   in an isolated worktree. Merge `origin/main` there with `git merge --no-edit origin/main`.
2. Verify `git merge-base --is-ancestor origin/main HEAD`. Inspect both parents with
   `git show --no-patch --format='%H %P' HEAD` and compare `git diff origin/develop HEAD`.
   A release ancestry repair should preserve the reviewed runtime tree; explain every file change.
3. Obtain independent manual review, push through the installed review hook, and open the
   completed PR template against `develop`. Merge only through
   `scripts/pr-merge-gate.sh piwas-21/restaurant-app-backend <pr> --merge` from the workspace.
   The gate detects this backmerge and uses a merge commit; squashing loses the required ancestry.
4. Re-fetch the branches and run `git merge-base --is-ancestor origin/main origin/develop`
   before creating or merging the next `develop` → `main` release PR. Recheck its diff, CI,
   review threads and Sonar issue count.

Keep shared clones untouched. Do not use direct protected-branch pushes or `gh pr update-branch`
to repair ancestry. An absent Sonar check, `NONE`, or zero populated conditions is an unanalysed
scope; wait for a real analysis rather than treating an empty issue list as approval.

## Configuration

| Setting | Source | Notes |
|---|---|---|
| Connection strings | `app-secrets.json` (gitignored) | `dev-secrets.sh` bootstraps from template |
| `EmailSettings` | `app-secrets.json` + `appsettings.<Env>.json` | `FrontendBaseUrl`, `BackendBaseUrl` are `[Required] [Url]` — no localhost defaults |
| `JwtSettings` | `app-secrets.json` | Secret must be ≥ 32 bytes |
| `CorsSettings:AllowedOrigins` | `appsettings.<Env>.json` | App throws on startup in non-Dev if empty |
| `SENTRY_DSN` (+ optional `SENTRY_ENVIRONMENT`) | env var (box `.env` → compose passthrough, deploy repo) | Empty/unset = Sentry never initializes (inert). Errors only — no PII, no request bodies, tracing off. Enable runbook: deploy repo `DEPLOYMENT.md` §Error tracking |
| `ReservationQuickActions` | `appsettings.json` + env override | Signs the approve/reject links in the restaurant's alert mail. See below |
| `TranslationAssistance` | env (`TranslationAssistance__*`) | Optional tenant menu translation provider; disabled until provider terms, processing region and tenant data use are approved. See below |

### Menu translation assistance

The admin translation workbench previews missing and stale guest-locale text without a provider. Both enablement switches default to false; leave them false until provider billing, processing region and tenant data terms are confirmed. Provider credentials belong in the box secret environment, not source control. The API does not log prompts or keys.

Choose one provider with `TranslationAssistance__Provider` (`openai` by default, or `gemini`). Configure its settings before setting both `TranslationAssistance__Enabled=true` and `TranslationAssistance__TenantDataApproved=true`:

| Provider | Required settings | Request behavior |
|---|---|---|
| OpenAI | HTTPS `TranslationAssistance__ApiUrl`, `TranslationAssistance__ApiKey`, model `TranslationAssistance__Model`, `TranslationAssistance__InputCostPerMillionUsd`, `TranslationAssistance__OutputCostPerMillionUsd` | Sends `store:false`; root cost defaults apply only to this provider. |
| Gemini | HTTPS base `TranslationAssistance__Gemini__ApiBaseUrl`, `TranslationAssistance__Gemini__ApiKey`, `TranslationAssistance__Gemini__Model`, `TranslationAssistance__Gemini__InputCostPerMillionUsd`, `TranslationAssistance__Gemini__OutputCostPerMillionUsd` | Uses GenerateContent with structured JSON output and low thinking; both nested cost rates must be positive before it is ready. Gemini thought tokens count toward output usage. |

The Gemini model defaults to `gemini-3.8-flash`, but its base URL, key and rates have no enabled defaults. The provider is selected at process startup; changing it requires a backend restart. Do not set either provider live until its current pricing and data-processing terms are verified.

`TranslationAssistance__MaxDailyBatches`, `MaxDailyTokens`, `MaxDailySpendUsd`, `MaxBatchTargets` and `TimeoutSeconds` bound each tenant database's usage. The workbench remains usable for manual edits and Save when the provider is disabled, over budget or unavailable. Suggestions require admin review and only reach guest text through the ordinary product or bundle Save.

Set the selected provider's cost fields for its model and processing tier before enabling it. The root defaults match standard GPT-6 Luna text pricing for OpenAI; regional processing can cost more. A provider attempt reserves the maximum configured output and a conservative input estimate before the call. Unknown outcomes keep that reservation charged for the day; successful calls settle to reported token usage.

### Reservation quick-action links

`GET /api/reservations/{id}/quick-approve` and `.../quick-reject` remain anonymous email entry
routes. Authority is a version2 HMAC-SHA256 token over booking ID, action, current status, expiry
and the **actual saved database UUID revision**. Status returning to Pending cannot revive an old
link. Both initial lookup and fresh locked dispatch verify the same original token.

All parent/combined-table saves finish before writers refresh the revision used by mailers. A
Pending customer contact-only or no-op edit refreshes operator buttons; Confirmed contact-only
edits retain confirmation and send no operator alert. Mail follows commit.

| Key | Default | Meaning |
| --- | --- | --- |
| `ReservationQuickActions:SigningKey` | empty | Purpose-separated HKDF derivation uses the configured key, or the already-required `JwtSettings:Secret`. No new environment setting is required. |
| `ReservationQuickActions:LinkLifetimeDays` | `7` | New link lifetime; after expiry the operator uses the reservations dashboard. |
| `ReservationQuickActions:LegacyLinkGraceDays` | `14` | Accepted configuration compatibility only; cannot grant revisionless authority. |
| `ReservationQuickActions:LegacyLinkCutoffUtc` | unset | Accepted configuration compatibility only; cannot grant revisionless authority. |

**Release compatibility:** unsigned and old status-only version1 links are refused conservatively,
even inside their former grace window. Previously issued links, or current links invalidated by
later staff/booking changes, open the uniform unavailable page with dashboard recovery. Do not
restore an old signing/unsigned fallback to revive them. New version2 mail works after the revision
migration already required by owned cancellation; this fix adds no migration or tenant activation.

### Native table payment account reads

`GET /api/customer-mobile/table-payments/account` requires schema1, the configured tenant UUID,
the original table-visit UUID, a canonical `X-Session-Id`, and a separate live `X-Table-Participant`.
Customer authentication never replaces the participant grant; invalid, staff or deleted-account
credentials cannot fall back to guest access. Current native capabilities, currency and participant
authority are checked again after the shared ledger read. Responses are private and `no-store`.

The response wraps the shared server account, including reserved contribution amounts and redacted
foreign attempt summaries. Schema1 refuses inexact JavaScript integers, more than 5,000 allocation
segments, 1,000 active summaries or 1,000 shares, and unsupported allocation versions. These are
response limits; the existing shared debt reader still scans the visit ledger without a row bound.
Account reads do not create a quote, reserve money or authorize provider navigation. Durable native
quote/reserve/start/recovery, all payment modes and their installed/provider acceptance remain open.
The endpoint adds no migration, configuration activation or change to web/staff/printer contracts.

## Pull requests

Every pull request uses [.github/pull_request_template.md](.github/pull_request_template.md). Include acceptance coverage, validation and deployment notes before creating it with the GitHub UI or `gh pr create`.

Required sections: summary, sprint-task link, acceptance-criteria coverage, schema/contract verification (for DB/DTO changes), standard checklist, test plan, deploy notes.

## CI gates

The gates are the workflows, not a plan doc: [.github/workflows/ci.yml](.github/workflows/ci.yml) (dotnet format,
file-length, `dotnet build` warnings-as-errors, sharded integration tests + merged-coverage floors, gitleaks,
TruffleHog, Trivy fs, license compliance, OSV-Scanner),
[.github/workflows/security-audit.yml](.github/workflows/security-audit.yml) (weekly full-tree OSV + Trivy fs +
NuGet vulnerability audit + license compliance), [.pre-commit-config.yaml](.pre-commit-config.yaml) (the same
format / file-length / build gates locally) and SonarCloud automatic analysis (populated passing PR quality gate
and zero open PR issues enforced by the
workspace merge gate). Cross-repo gate status lives in the workspace
[DEV-PHASES-PLAN.md](../docs/plans/DEV-PHASES-PLAN.md) §2 — that table is authoritative.

Integration shard retries overwrite the matching result artifact so the aggregate uses the replacement discovery
counts and coverage report. Rerun the failed shard with the partition, uniqueness and coverage guards intact.
For a legacy run with duplicate names, preserve both artifact ZIPs, digests and metadata, verify the obsolete copy
belongs to the failed attempt, remove only that incomplete copy, then rerun the aggregate against the successful
replacement. Never alter discovery counts, test reports or coverage floors to make a retry pass.

Integration tests are partitioned by namespace: shard 1 owns Common, Products, Payments, Menus,
DeliveryChannels, ServerWorkspace and Account-prefixed Orders classes; shard 2 owns CustomerNativeReservation-prefixed
Reservations classes and the remaining namespaces; shard 3 owns other Orders classes and legacy Reservations;
shard 4 owns Basket, Auth and Customer-prefixed Orders classes. The aggregate checks discovery counts and unique execution across the complete suite.

Each integration shard has a 30-minute whole-job budget covering restore/build, tests, coverage/discovery and artifact uploads. Test selection, assertions and merged-coverage floors remain enforced.

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
