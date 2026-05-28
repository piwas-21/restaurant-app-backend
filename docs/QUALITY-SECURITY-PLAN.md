# Backend — Quality & Security Hardening Plan

Stack: **.NET 10**, **EF Core 10**, **PostgreSQL**, custom CQRS mediator. Hosted on GitLab.

> Read [/QUALITY-SECURITY-PLAN.md](../../docs/plans/QUALITY-SECURITY-PLAN.md) first for cross-repo context. This document only adds the .NET-specific tasks.

---

## 0. Current state

- `.gitlab-ci.yml` (79 lines): gitleaks, GitLab SAST template (auto-injected analyzers), `docker build`, `trivy image` (allow_failure)
- `gitleaks.toml`: 4-line stub, no rules / no allowlist
- No pre-commit, no formatter gate, no analyzer fail-on-warning, no coverage gate
- xUnit + Coverlet are referenced in [TEST-COVERAGE-PLAN.md](TEST-COVERAGE-PLAN.md) but not wired into CI
- `RestaurantSystem.IntegrationTests` exists; tests run only locally

## 1. Tooling decisions

| Concern | Tool | Why |
|---|---|---|
| Format | `dotnet format` | built-in, deterministic, respects `.editorconfig` |
| Lint / static analysis | Roslyn analyzers (`Microsoft.CodeAnalysis.NetAnalyzers`) + `SonarAnalyzer.CSharp` | catches FxCop-class issues + Sonar rules locally |
| Security analyzer | `SecurityCodeScan.VS2019` (or successor) | injection, XSS, weak crypto, deserialization |
| Test runner | `dotnet test` (xUnit) | already adopted |
| Coverage | `coverlet.collector` → opencover.xml + cobertura | GitLab `coverage_report:cobertura` for MR decoration |
| Coverage report | `dotnet-reportgenerator-globaltool` | merged HTML + summary for artifact |
| Vuln check | `dotnet list package --vulnerable --include-transitive` | first-party advisory feed |
| SCA | OSV-Scanner (`packages.lock.json`) | catches CVEs missing from NuGet feed |
| SBOM | `Microsoft.Sbom.Tool` (Component Detection) | SPDX 2.3 SBOM artifact |
| SAST | SonarCloud + Roslyn `SonarScanner.MSBuild` | quality gate |
| Secret scan | gitleaks (existing) + detect-secrets (pre-commit) | belt + suspenders |
| Image scan | trivy image (existing) | upgrade to fail-on-critical |

## 2. Repository-level changes

### 2.1 Add `.editorconfig` (root of `backend/`)
Single source of truth for formatting. Include:
- `csharp_style_*` enforcing the conventions in [CLAUDE.md](../../CLAUDE.md) (no `null!`, `required` modifier preferred, PascalCase, `Async` suffix)
- `dotnet_diagnostic.CA*` severities: warnings as errors for security-sensitive rules (CA2100, CA3001-3012, CA5350+)
- File-scoped namespaces, expression-bodied members for one-liners

### 2.2 Add `Directory.Build.props` (root of `backend/`)
Apply analyzer packages once across all 4 csproj files:
```xml
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <Nullable>enable</Nullable>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="..." PrivateAssets="all"/>
    <PackageReference Include="SonarAnalyzer.CSharp" Version="..." PrivateAssets="all"/>
    <PackageReference Include="SecurityCodeScan.VS2019" Version="..." PrivateAssets="all"/>
  </ItemGroup>
</Project>
```
Lockfile (`packages.lock.json` per project) is the input to OSV-Scanner.

### 2.3 Replace `gitleaks.toml`
- Extend the default config (`[extend] useDefault = true`)
- Allowlist:
  - `RestaurantSystem.Api/appsettings.Development.json` (only if it contains placeholder secrets — otherwise fail)
  - test fixtures under `RestaurantSystem.IntegrationTests/Fixtures/`
- Block: any string matching JWT, AWS key, SMTP password patterns

### 2.4 File-length checker (port of DeelMarkt's `check_quality.dart`)
A small `scripts/check-quality.ps1` + `.sh` that walks `**/*.cs` and fails if any file exceeds the [CLAUDE.md](../../CLAUDE.md) limits:
- Controller > 150 LOC
- Handler > 200 LOC
- Service > 300 LOC
- Entity > 100 LOC
- DTO/Record > 60 LOC
- Validator > 60 LOC
- Configuration > 50 LOC

It also greps for forbidden patterns:
- `throw new InvalidOperationException` outside `RestaurantSystem.Domain/`
- raw `DbContext` injection in `*Controller.cs`
- hardcoded `http(s)://` URLs in `Features/**/*.cs`

## 3. Pre-commit hooks

`.pre-commit-config.yaml` (in `backend/`) — adds these `local` hooks on top of the cross-repo general+secrets sections:

```yaml
- id: dotnet-format
  name: dotnet format (verify-no-changes)
  language: system
  entry: bash -c 'dotnet format RestaurantSystem.sln --verify-no-changes --no-restore'
  pass_filenames: false
  files: \.(cs|csproj|sln)$
  stages: [pre-commit]

- id: dotnet-build
  name: dotnet build (warnings as errors)
  language: system
  entry: bash -c 'dotnet build RestaurantSystem.sln --no-restore -warnaserror'
  pass_filenames: false
  files: \.(cs|csproj)$
  stages: [pre-commit]

- id: file-length-rules
  name: backend file length / pattern rules
  language: system
  entry: bash scripts/check-quality.sh
  pass_filenames: false
  files: \.cs$
  stages: [pre-commit]

# Pre-push only — slower
- id: dotnet-test-affected
  name: dotnet test (affected projects)
  language: system
  entry: bash scripts/test-affected.sh
  pass_filenames: false
  files: \.(cs|csproj)$
  stages: [pre-push]

- id: vulnerable-packages
  name: dotnet list package --vulnerable
  language: system
  entry: bash -c 'dotnet list RestaurantSystem.sln package --vulnerable --include-transitive | tee /tmp/vuln.txt; ! grep -E ">.*Critical|>.*High" /tmp/vuln.txt'
  pass_filenames: false
  files: (\.csproj|packages\.lock\.json)$
  stages: [pre-push]
```

`scripts/test-affected.sh` mirrors DeelMarkt's `test_affected.dart`:
- For each changed `.cs` file under `RestaurantSystem.<Project>/...`, run `dotnet test RestaurantSystem.IntegrationTests --filter "FullyQualifiedName~<Feature>"`
- Bail to running the full suite if mapping is ambiguous (e.g. `Common/` changes)

## 4. GitLab CI

Replace the existing `.gitlab-ci.yml` with:

### Stages
```
lint → test → security → sast → build → scan → deploy
```

### Jobs (sketch — only the keys, not the YAML body)

| Stage | Job | Image | Blocking? |
|---|---|---|---|
| lint | `format` | `mcr.microsoft.com/dotnet/sdk:10.0@sha256:...` | yes |
| lint | `analyze` (`dotnet build -warnaserror`) | sdk:10.0 | yes |
| lint | `quality-rules` (file-length script) | alpine | yes |
| test | `unit-tests` (`dotnet test --collect:"XPlat Code Coverage"`) | sdk:10.0 | yes |
| test | `coverage-gate` (lcov merge → fail < 70% line) | sdk:10.0 | yes |
| security | `gitleaks` | `zricethezav/gitleaks:v8.x@sha256:...` | yes |
| security | `osv-scanner` (lockfiles) | `ghcr.io/google/osv-scanner@sha256:...` | yes |
| security | `dotnet-vuln` (`dotnet list package --vulnerable`) | sdk:10.0 | yes |
| security | `trivy-fs` (`trivy fs --severity CRITICAL,HIGH --exit-code 1`) | aquasec/trivy | yes |
| security | `sbom` (Microsoft.Sbom.Tool) | sdk:10.0 | no, artifact |
| sast | `sast` (GitLab template — keep) | autodevops | yes |
| sast | `sonarcloud` (SonarScanner.MSBuild) | sdk:10.0 | yes (quality gate) |
| build | `build_image` (existing) | docker:24 | yes |
| scan | `trivy_image` (existing — flip to `exit-code: 1`) | docker:24 | yes |
| deploy | `trigger_argocd` (existing — uncomment) | n/a | manual |

### Coverage report wiring
```yaml
unit-tests:
  artifacts:
    reports:
      coverage_report:
        coverage_format: cobertura
        path: '**/coverage.cobertura.xml'
      junit: '**/TestResults/*.trx'
```

### Concurrency
```yaml
workflow:
  rules:
    - if: $CI_PIPELINE_SOURCE == 'merge_request_event'
    - if: $CI_COMMIT_BRANCH == 'main'
    - if: $CI_COMMIT_BRANCH == 'dev'
default:
  interruptible: true
```

## 5. Weekly scheduled audit (`.github/workflows/security-audit.yml`)

**Implemented** (issue #15) — runs on GitHub Actions, `schedule: '0 6 * * 1'` (Mondays 06:00 UTC) + `workflow_dispatch` (manual). `permissions: contents: read`, concurrency-guarded, per-job timeouts, all actions SHA-pinned (same pins as `ci.yml` where reused). It re-runs the security scanners on the **full tree** independent of PR activity, so vulnerabilities in untouched code and newly-disclosed CVEs in pinned dependencies surface even when no PR touches them.

| Job | Tool | Scope | Failure Condition |
|---|---|---|---|
| `osv_scanner` | OSV-Scanner (`google/osv-scanner-action`) | full tree (`--recursive .`) | known CVE |
| `trivy_fs` | Trivy fs (`aquasecurity/trivy-action`) | full tree | HIGH/CRITICAL (fixed) |
| `nuget_vuln_audit` | `dotnet list package --vulnerable --include-transitive` | transitive dependencies | HIGH/CRITICAL advisory |
| `license_compliance` | `nuget-license` v4.0.0 + `LICENSES.*` | project refs | disallowed license |

What's **new** vs the per-PR gates in `ci.yml`:
- **`nuget_vuln_audit`** — surfaces the NU1901–NU1904 advisories that `Directory.Build.props` downgrades from build errors on per-PR builds (so they don't block unrelated PRs). `dotnet list package --vulnerable` exits 0 even with findings, so the job captures the output, writes it to the Job Summary, and fails explicitly on High/Critical (reports Low/Moderate without failing).
- **Full-tree** OSV scan (the per-PR scan may be diff-scoped).

How failures surface: a red ❌ on the Actions tab is the alert. Each job writes a Job Summary with the finding; triage and patch on the dependency-upgrade track, or document an accepted-risk suppression. Failure is intentional signal — nothing auto-merges and no automation opens issues (keeps permissions minimal, no `issues: write` injection surface).

Deferred (not yet ported from the original GitLab sketch below): TruffleHog full-history, `dotnet list package --outdated` artifact, sensitive-file audit, `trivy config` over Dockerfiles/k8s manifests.

<details><summary>Original GitLab sketch (superseded)</summary>

`.gitlab-ci.weekly.yml` via include — cron `0 6 * * 1`:
- TruffleHog full history (`--results=verified,unknown`)
- OSV-Scanner JSON, retain 30 days
- `dotnet list package --outdated` → artifact
- License audit: `dotnet-project-licenses --include-transitive --forbidden-license GPL-2.0-only --forbidden-license AGPL-3.0`
- Sensitive-file audit (mirrors DeelMarkt's `infra-security` job)
- `trivy config` over `Dockerfile`, `docker-compose*.yml`, and the `rumi-argocd-gitops` k8s manifests
</details>

## 6. SonarCloud config (`backend/sonar-project.properties`)

```
sonar.projectKey=rumi_backend
sonar.organization=<rumi-org>
sonar.sources=RestaurantSystem.Api,RestaurantSystem.Domain,RestaurantSystem.Infrastructure
sonar.tests=RestaurantSystem.IntegrationTests
sonar.cs.opencover.reportsPaths=**/coverage.opencover.xml
sonar.cs.vstest.reportsPaths=**/TestResults/*.trx
sonar.exclusions=**/Migrations/**,**/obj/**,**/bin/**
sonar.coverage.exclusions=\
  RestaurantSystem.Api/Program.cs,\
  RestaurantSystem.Api/BackgroundServices/**,\
  **/Migrations/**,\
  **/Configuration/**
```
Quality gate: A-rating, no new bugs/vulnerabilities, ≥ 70% new-code coverage.

## 7. Phased task breakdown

### Sprint 1 (hygiene + dev-experience baseline)
1. Commit cross-repo `.pre-commit-config.yaml` (general + secrets)
2. Generate `.secrets.baseline`, list paths in `.secrets-scan-paths` (`RestaurantSystem.*/`, `scripts/`)
3. Replace `gitleaks.toml` with real config
4. Add `scripts/setup_hooks.sh` + `.ps1`
5. Document branch protection in `backend/README.md`
6. Add `.gitlab/merge_request_templates/Default.md` — Schema/Contract Verification section names: EF migration files (`Migrations/<timestamp>_<name>.cs`), affected DTO records under `Features/**/Dtos/`
7. Add `scripts/dev-up.sh` + `dev-down.sh` + `dev-secrets.sh` (`.ps1` mirrors). `dev-up.sh` orchestrates: `docker compose -f docker-compose-dev-all.yml up -d` → `pg_isready` poll (timeout 30s, friendly error if exceeded) → `dotnet ef database update --project RestaurantSystem.Infrastructure` → `dotnet run --project RestaurantSystem.Api` (skipped with `--no-run`). `--reset` drops the postgres volume.
8. Update `backend/README.md` with one-liner onboarding: `bash scripts/setup_hooks.sh && bash scripts/dev-up.sh`
9. Create `docs/adr/` + `ADR-template.md` + `README.md` index. Backfill ADRs:
   - **ADR-001** Custom CQRS mediator (vs MediatR) — why no `Send<TResponse>(IRequest)` overhead, how `ICustomMediator` resolves handlers
   - **ADR-002** Soft-delete `IsDeleted` global query filter — query semantics, restore procedure
   - **ADR-003** JWT scope shape + claims — what's in the token, what isn't, why
   - (ADR-004 and ADR-005 land in Sprint 2)

### Sprint 2 (format + lint + reusable CI templates)
10. Add `.editorconfig` mapping CLAUDE.md rules to analyzer severities
11. Add `Directory.Build.props` with analyzers + `TreatWarningsAsErrors`
12. Add `scripts/check-quality.sh` (file-length + forbidden-pattern checks)
13. Add `dotnet format`, `dotnet build -warnaserror`, `quality-rules` to pre-commit
14. **Add `.gitlab/ci/setup-dotnet.yml`** with `.setup-dotnet` job template: pinned `mcr.microsoft.com/dotnet/sdk:10.0@sha256:...`, NuGet cache (`~/.nuget/packages` keyed by `*.csproj` hash), `dotnet restore`, `dotnet tool restore`. `include:` it from `.gitlab-ci.yml`.
15. Add `lint` stage to `.gitlab-ci.yml` (format / analyze / quality-rules jobs) — all using `extends: .setup-dotnet`
16. Pin all remaining images in `.gitlab-ci.yml` by digest
17. Backfill remaining ADRs:
   - **ADR-004** BackgroundService cleanup intervals + retention windows (BasketCleanup, AccountCleanup, TableReservationCleanup) — data-loss class, needs explicit decision record
   - **ADR-005** Custom exception hierarchy (NotFoundException / BadRequestException / ForbiddenException) — why we don't use `InvalidOperationException` for user-facing errors

### Sprint 3 (test + coverage + SAST)
12. Wire Coverlet → opencover + cobertura in `RestaurantSystem.IntegrationTests`
13. Add `unit-tests` + `coverage-gate` jobs (≥ 70% line)
14. Add `scripts/test-affected.sh` + pre-push hook
15. Create SonarCloud project, commit `sonar-project.properties`
16. Add `sonarcloud` job; enable MR decoration
17. Add new-code coverage pre-push hook (port DeelMarkt's logic to bash + cobertura)

### Sprint 4 (deep security)
18. Add `osv-scanner`, `dotnet list package --vulnerable`, `trivy fs` jobs
19. Flip `trivy image` from `allow_failure: true` to blocking
20. Add `sbom` job (Microsoft.Sbom.Tool), 30-day artifact retention
21. Add weekly schedule pipeline (TruffleHog full, OSV JSON, outdated, license, sensitive-file audit, `trivy config`)
22. Verify all GitLab CI images pinned by digest; add `scripts/pin_ci_images.sh`

## 8. Acceptance criteria

- [ ] `pre-commit run --all-files` green from clean checkout
- [ ] MR pipeline blocks on any: format, analyzer warning, vulnerable package (Critical/High), failing test, coverage < 70%, SonarCloud quality gate red
- [ ] `dotnet build -warnaserror` succeeds on all projects
- [ ] No `null!`, no `InvalidOperationException` for user-facing errors, no raw `DbContext` injection in controllers (enforced by `check-quality.sh` + Roslyn analyzers)
- [ ] Weekly pipeline produces SBOM, OSV JSON, outdated-deps, and license-audit artifacts
- [ ] No `:latest` or floating tag in `.gitlab-ci.yml`
