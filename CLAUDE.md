# RUMI Backend — Agent Rules

> Auto-loaded by Claude Code on every session in this repository. These rules apply to ALL code changes in `backend/`.
> First read on a cold session: this file → [docs/SPRINT-PLAN.md](docs/SPRINT-PLAN.md) (refactoring track) + the sprint task you're picking up.

---

## §1 — Identity

- **Stack**: .NET 10, EF Core 10, PostgreSQL, custom CQRS mediator (`CustomMediator` — **NOT MediatR**)
- **Architecture**: Clean Architecture (API → Domain → Infrastructure) + CQRS + feature folders
- **Hosted on**: GitHub — https://github.com/piwas-21/restaurant-app-backend
- **Production**: deployed from `main` (currently `develop` until cutover); test environment from `develop`
- **In-flight workspace**: this repo is one of three under [/Users/mahmutkaya/workspace/rumi-workspace/](../). The workspace meta-repo holds cross-repo plans and the master roadmap. When this repo is cloned standalone, only this `CLAUDE.md` is in scope.

## §2 — Critical files to read

| When | Read |
|---|---|
| Any task | This file |
| Refactoring sprint task | [docs/SPRINT-PLAN.md](docs/SPRINT-PLAN.md) — find the task ID, read its acceptance criteria |
| File-level audit context (god classes, DRY, design system) | [../docs/plans/RUMI-ANALYSIS-AND-PLAN.md](../docs/plans/RUMI-ANALYSIS-AND-PLAN.md) (workspace meta-repo) |
| Quality/security gate work | [docs/QUALITY-SECURITY-PLAN.md](docs/QUALITY-SECURITY-PLAN.md) |
| Test work | [docs/TEST-COVERAGE-PLAN.md](docs/TEST-COVERAGE-PLAN.md) |
| Security review / threat model | [docs/SECURITY-AUDIT.md](docs/SECURITY-AUDIT.md) |
| Architectural decisions | [docs/adr/README.md](docs/adr/README.md) — index of ADRs |
| Starting a session | Run `dotnet build RestaurantSystem.sln` to establish baseline |
| Bug fix / feature | Read the relevant ADR if one exists for the affected subsystem |

---

## §3 — Architecture

### Layer dependencies (never bypass)

```
RestaurantSystem.Api/        ← API layer (Controllers, CQRS handlers, Features)
  └── RestaurantSystem.Domain/   ← pure domain (Entities, enums) — no EF, no ASP.NET
  └── RestaurantSystem.Infrastructure/  ← EF Core, persistence, migrations
       └── (depends on Domain)
RestaurantSystem.IntegrationTests/  ← integration tests (xUnit)
```

- **API → Domain → Infrastructure** dependency direction; never reversed.
- Domain layer is **pure C#** — no `Microsoft.EntityFrameworkCore`, no `Microsoft.AspNetCore.*` references.

### Feature folder layout

Every feature under `RestaurantSystem.Api/Features/<X>/` follows:

```
Features/<X>/
├── Commands/
│   └── <DoSomethingCommand>/
│       ├── <DoSomethingCommand>.cs        # record + handler in same file
│       └── <DoSomethingCommandValidator>.cs   # FluentValidation
├── Queries/
│   └── <GetSomethingQuery>/
│       └── <GetSomethingQuery>.cs         # record + handler in same file
├── Dtos/
│   └── <X>Dto.cs                          # one DTO per file (or grouped sub-records)
├── Services/
│   ├── I<X>Service.cs                     # interface
│   └── <X>Service.cs                      # implementation
├── Interfaces/                            # any other interfaces
└── <X>Controller.cs                       # thin dispatcher, max 150 LOC
```

Active features (as of writing):
`Addresses` · `Auth` · `Basket` · `Categories` · `Email` · `FidelityPoints` · `GlobalIngredients` · `Groups` · `Menus` · `Orders` · `Products` · `Reservations` · `Settings` · `User`

### Custom mediator (NOT MediatR)

Defined in [RestaurantSystem.Api/Abstraction/Messaging/](RestaurantSystem.Api/Abstraction/Messaging/) and [RestaurantSystem.Api/Common/CustomMediator.cs](RestaurantSystem.Api/Common/CustomMediator.cs):

```csharp
public interface ICommand;                                  // void command
public interface ICommand<TResult> { }                      // command with result
public interface ICommandHandler<TCommand, TResult> { ... }
public interface IQuery<TResult> { }
public interface IQueryHandler<TQuery, TResult> { ... }
```

Controllers dispatch via:
```csharp
var result = await _mediator.SendCommand(command);   // for commands
var result = await _mediator.SendQuery(query);       // for queries
```

Do NOT add the `MediatR` NuGet package back. See [docs/adr/ADR-001-custom-cqrs-mediator.md](docs/adr/ADR-001-custom-cqrs-mediator.md).

### Background services (data-loss class — handle with care)

- `BasketCleanupService` — purges abandoned baskets
- `AccountCleanupService` — finalises pending account deletions
- `TableReservationCleanupService` (planned) — releases stale reservations

These run on timers and **delete** records. Never modify retention windows or polling intervals without explicit approval (see §9 AI guardrails).

### Soft delete

All soft-delete-aware entities use `IsDeleted` with a global query filter in `ApplicationDbContext`. Never bypass with `IgnoreQueryFilters()` unless restoring records. See [docs/adr/ADR-002-soft-delete-strategy.md](docs/adr/ADR-002-soft-delete-strategy.md).

---

## §4 — File length limits

Enforced by [scripts/check-file-length.sh](scripts/check-file-length.sh) (pre-commit + CI `file_length` job, blocking).

| File type | Max LOC | Action if exceeded |
|---|---|---|
| Controller | 150 | Extract handlers — controllers are dispatchers, not logic |
| Command/Query handler | 200 | Decompose into use-case + repository methods |
| Service class | 300 | Split by responsibility (target: one service = one concern) |
| Entity (Domain) | 100 | Decompose; the model is doing too much |
| DTO / Record | 60 | Split into focused DTOs |
| Validator | 60 | Split per command if multi-aspect |
| Configuration class (`*Settings.cs`) | 50 | Group related settings into separate classes |

**Existing oversized files** are baselined in [scripts/file-length-baseline.txt](scripts/file-length-baseline.txt) (set at the current honest floor; ratchet down as the refactor track lands). New violations block the gate.

**Per-file opt-out** (rare; needs reviewer sign-off): add `// FILE_LENGTH_EXEMPT: <reason>` within the first 5 lines of the file.

**After a refactor lands** that brings a baselined file under its limit:
```bash
bash scripts/check-file-length.sh --regen-baseline
```
Commit the updated `scripts/file-length-baseline.txt` in the same MR.

---

## §5 — Backend rules (hard)

1. **Controllers are thin dispatchers** — max 150 LOC, no business logic, no `DbContext` injection, no inline EF queries.
2. **One command/query per file** with handler in the same file (or same directory).
3. **Custom mediator** — `_mediator.SendCommand` / `_mediator.SendQuery`. Never `MediatR`.
4. **Use custom exceptions for user-facing errors**:
   - `NotFoundException` → maps to 404
   - `BadRequestException` → maps to 400
   - `ForbiddenException` → maps to 403
   - **Never** `InvalidOperationException` outside of true programmer errors.
5. **All services have interfaces**; register via `Program.cs` extension methods.
6. **No raw `DbContext`** in controllers. Use CQRS handlers, which inject `ApplicationDbContext` themselves.
7. **CORS** must specify `CorsSettings:AllowedOrigins` in production. `Program.cs` throws on startup if non-Dev with empty list.
8. **Admin email** comes from `IOptions<EmailSettings>.Value.AdminEmail`. Never hardcoded literals.
9. **URLs in emails** come from `IOptions<EmailSettings>.Value.{Frontend,Backend}BaseUrl`. Both `[Required] [Url]`, no defaults.
10. **Validator naming**: `{CommandName}Validator.cs` (no `CommandValidator` suffix duplication).
11. **No hardcoded secrets, emails, URLs, or magic numbers** in source. Configuration via `IOptions<T>` only.
12. **No `null!` in DTOs** — use `required` modifier or `= string.Empty`.
13. **Audit identifiers**: use `ICurrentUserService.GetAuditIdentifier()` instead of inline `UserId?.ToString() ?? "System"`.

---

## §6 — Pre-implementation verification (REQUIRED for non-trivial work)

> Output this checklist BEFORE writing any implementation code. Skipping = restart the task.
> "Non-trivial" = anything beyond a one-line typo / comment fix.

### 1. Schema verification (any change touching DB or DTOs)
For each DB column / DTO field referenced, name the source of truth:
- **EF migration** path (`RestaurantSystem.Infrastructure/Persistence/Migrations/<timestamp>_<name>.cs`)
- Field name + type as it appears there
- For joins / nested DTOs, confirm both sides match.

### 2. Sibling conventions
List 2–3 sibling files in the directory you're adding to. Note their structure (DI registration, naming, base class). Confirm your new file matches.

### 3. Acceptance criteria audit
Quote the relevant criteria from the sprint task / issue. Mark each:
- **Covered fully** (this PR closes it)
- **Partial** (note what's missing, link follow-up)
- **Out of scope** (note where it'll land)

### 4. Existing references
Grep for the type/method/key you're adding or modifying. List every callsite. Confirm each still works after your change OR mark for update in this PR.

### 5. Cross-cutting check
- Does this affect the `printer-app` repo (DTO contract)?
- Does this affect the `frontend` repo (API contract)?
- If yes, flag in the MR description as "breaking" or "additive".

---

## §7 — Quality gates

| Gate | When | What | Blocking? | Source of truth |
|---|---|---|---|---|
| `dotnet build RestaurantSystem.sln` (warnings as errors) | Pre-commit (when .cs/.csproj/.sln/.props/.targets staged) **and** CI workflow (`dotnet_build_strict` job) | 0 errors, 0 non-excluded warnings | yes | [Directory.Build.props](Directory.Build.props), `.github/workflows/ci.yml` |
| `dotnet test RestaurantSystem.IntegrationTests` | CI workflow (`dotnet_test` job, dind service for Testcontainers) | All non-skipped tests pass | yes | `.github/workflows/ci.yml` |
| Coverage threshold (coverlet, line ≥ 17% / branch ≥ 9% / method ≥ 15%, migrations excluded) | CI workflow (same `dotnet_test` job) | No regression below the current floor | yes | `.github/workflows/ci.yml` |
| `dotnet format --verify-no-changes` | Pre-commit (when .cs/.csproj/.sln staged) **and** CI workflow (`dotnet_format` job) | 0 formatting drift | yes | [.pre-commit-config.yaml](.pre-commit-config.yaml), `.github/workflows/ci.yml` |
| File-length gate | Pre-commit (per-file when .cs staged) **and** CI workflow (`file_length` job) | LOC ≤ §4 limit OR file is in `scripts/file-length-baseline.txt` | yes | [scripts/check-file-length.sh](scripts/check-file-length.sh), [.pre-commit-config.yaml](.pre-commit-config.yaml), `.github/workflows/ci.yml` |
| Pre-commit hooks | Every `git commit` | trailing whitespace, EOF, large files, secret scan, no-commit-to-protected | yes | [.pre-commit-config.yaml](.pre-commit-config.yaml) |
| CodeQL (SAST) | CI workflow | Auto-injected analyzers | yes | `.github/workflows/ci.yml` |
| Gitleaks | CI workflow | No leaked credentials (allowlist via `.gitleaks.toml`) | yes | [.gitleaks.toml](.gitleaks.toml) |
| Trivy image scan | After build | Reports CRITICAL/HIGH CVEs | **no** (currently `allow_failure: true`) | `.github/workflows/ci.yml` |

Trivy is non-blocking today — it surfaces findings without failing the pipeline. Sprint 4 of [docs/QUALITY-SECURITY-PLAN.md](docs/QUALITY-SECURITY-PLAN.md) flips it to `exit-code: 1` for CRITICAL/HIGH.

Analyzer warnings-as-errors gate fully blocking as of Sprint 3 via `TreatWarningsAsErrors` in [Directory.Build.props](Directory.Build.props). Only NuGet vulnerability advisories (NU1901–NU1904, dep-upgrade track) and EF migration class names (CS8981) remain excluded. Coverage gate landed (line ≥ 17%, branch ≥ 9%, method ≥ 15% — pinned at the current honest floor with migrations excluded; raise as coverage grows). SAST quality gate (SonarCloud) lands later in Sprint 3.

### Setup for a new developer
```bash
bash scripts/setup_hooks.sh   # installs pre-commit hooks (one-time)
bash scripts/dev-up.sh        # boots local DB + applies migrations + runs API
```

---

## §8 — Git workflow

### Branch strategy

```
main                    ← production (currently develop; cutover pending)
  └── develop           ← test environment (auto-deployed)
       ├── feature/<x>
       ├── fix/<x>
       ├── chore/<x>
       └── docs/<x>
```

- **Never push to `main` or `develop` directly** — pre-commit hook blocks this.
- Branch off **`develop`**. Open PR to `develop`. After merge to `develop` and test-env validation, `develop` is promoted to `main` for prod.
- One issue = one branch. Delete branch after merge (via `gh pr merge --delete-branch`, or enable "Automatically delete head branches" in the repo's General settings).
- Branch naming: `feature/`, `fix/`, `chore/`, `docs/`, `test/`.

### Commit messages

Format: `type(scope): description`

| Type | Use for |
|---|---|
| `feat` | New feature visible to user |
| `fix` | Bug fix |
| `refactor` | Code change with no behaviour change |
| `chore` | Build / CI / dependencies / config |
| `docs` | Documentation only |
| `test` | Tests only |
| `perf` | Performance improvement |

Body should explain **why**, not what (the diff shows what).

### Merge requests

Every PR uses [.github/pull_request_template.md](.github/pull_request_template.md). Required sections:
- Summary
- Sprint task / issue link
- Acceptance criteria coverage table
- Schema/contract verification (for DB or DTO changes)
- Standard checklist (build, lint, tests, no hardcoded secrets, sibling conventions matched)

---

## §9 — AI guardrails (refusal list)

Never auto-edit these files / take these actions without explicit user instruction:

### Hard refusals
- **EF migrations after they've been applied to staging or production.** Once a migration is in `Migrations/` and merged to `develop`, treat it as immutable. New schema changes = new migration.
- **`appsettings.Production.json`** (if it exists in repo). Production config changes are a deploy event, not a code change.
- **`app-secrets.json`** (gitignored) — never recreate, never commit. If missing, flag it; don't fabricate values.
- **`BackgroundServices/*.cs` retention windows / polling intervals** — data-loss class. Changes need explicit approval.
- **Branch protection bypass**: never `git commit --no-verify`, `git push --force-with-lease` to `develop`/`main`, `git reset --hard` on `develop`/`main`.

### Cross-repo coordination required
- **DTO field renames or removals** — affects `printer-app/Models/` (Models must mirror backend DTOs exactly per the printer-app rule). Before changing a DTO, grep `printer-app/PrinterAPP/Models/` for usages and flag the cross-repo impact in the MR.
- **API contract changes** — affects `frontend/src/services/`. Coordinate the deploy.

### Sensitive-file refusal (matches gitleaks/detect-secrets allowlist)
Never commit:
- `*.pem`, `*.key`, `*.pfx`, `*.p12`, `*.cer`, `*.snk`, `*.keystore`, `*.jks`
- `app-secrets*` (any variant)
- `.env*`

---

## §10 — Session workflow

### Starting
1. Read this file (auto-loaded).
2. Read [docs/SPRINT-PLAN.md](docs/SPRINT-PLAN.md) if picking up a sprint task.
3. Run `dotnet build RestaurantSystem.sln` — confirm baseline green.
4. Check `git status` — start from clean tree on `develop`.

### During implementation
1. Output the §6 verification block before writing code.
2. After each file change, the agent's PostToolUse hook (Sprint 2) will warn on file-length / forbidden-pattern violations.
3. Run `dotnet build` after non-trivial changes — catches type errors early.
4. Use `_emailSettings.AdminEmail`, never `"rumigeneve@gmail.com"`.
5. Use `_currentUserService.GetAuditIdentifier()`, never inline ternaries.

### Before ending
1. `dotnet build RestaurantSystem.sln` → 0 errors.
2. `git status` → only intentional changes staged.
3. Commit with `type(scope):` format.
4. Push to feature branch.
5. Open PR via `gh pr create` (or GitHub UI) — fill in the template fully, including acceptance-criteria coverage table.
