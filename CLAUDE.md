# RUMI Frontend — Agent Rules

> Auto-loaded by Claude Code on every session in this repository. These rules apply to ALL code changes in `frontend/`.
> First read on a cold session: this file → [docs/SPRINT-PLAN.md](docs/SPRINT-PLAN.md) (refactor track) + the issue/sprint task you're picking up.

---

## §1 — Identity

- **Stack**: Next.js 15.5 (App Router) · React 19 · TypeScript · CSS Modules · i18next (9 locales)
- **Test**: Jest + React Testing Library (unit) · Playwright (E2E)
- **Hosted on**: GitLab — https://gitlab.com/restaurant-app3282120/frontend
- **Production**: deployed from `main` (currently `develop` until cutover); test environment from `develop`
- **Backend dependency**: this app talks to the [backend repo](https://gitlab.com/restaurant-app3282120/backend) via `NEXT_PUBLIC_API_URL`. DTO contracts mirror backend `Features/**/Dtos/`.
- **In-flight workspace**: this repo is one of three under [/Users/mahmutkaya/workspace/rumi-workspace/](../). The workspace meta-repo holds cross-repo plans and the master roadmap. When this repo is cloned standalone, only this `CLAUDE.md` is in scope.

## §2 — Critical files to read

| When | Read |
|---|---|
| Any task | This file |
| Refactoring sprint task | [docs/SPRINT-PLAN.md](docs/SPRINT-PLAN.md) — find the task ID, read its acceptance criteria |
| Design / component patterns | [docs/DESIGN-SYSTEM.md](docs/DESIGN-SYSTEM.md) |
| Coding conventions | [docs/DEVELOPMENT-GUIDELINES.md](docs/DEVELOPMENT-GUIDELINES.md) |
| Test work | [docs/TEST-COVERAGE-PLAN.md](docs/TEST-COVERAGE-PLAN.md) |
| Quality / security gate work | [docs/QUALITY-SECURITY-PLAN.md](docs/QUALITY-SECURITY-PLAN.md) |
| Security review / threat model | [docs/SECURITY-AUDIT.md](docs/SECURITY-AUDIT.md) |
| Architectural decisions | [docs/adr/README.md](docs/adr/README.md) — index of ADRs |
| Bug or UX item | [../docs/plans/BUGS-IMPROVEMENTS-PLAN.md](../docs/plans/BUGS-IMPROVEMENTS-PLAN.md) (workspace meta-repo) — Track A/B/C items |
| Starting a session | Run `npm run lint && npm run build` to establish baseline |

---

## §3 — Architecture

### App Router structure

```
src/
├── app/                          # Next.js App Router routes
│   ├── (route-groups)/
│   ├── api/                      # API route handlers (proxy to backend)
│   ├── layout.tsx                # Root layout
│   └── globals.css               # Global CSS variables (design tokens)
├── components/                   # Shared UI components
│   ├── design-system/            # BaseModal, FormField, StatusBadge, etc.
│   ├── menu/                     # Feature-area folders
│   ├── cashier/
│   └── ...
├── contexts/                     # React Context providers (Auth, Cart, Theme, etc.)
├── hooks/                        # Custom hooks (page-level logic lives here)
├── lib/                          # apiClient, config, formatters
├── locales/                      # i18next JSON files (9 locales)
├── services/                     # Backend API service files (one per resource)
├── styles/                       # Shared CSS Modules + globals
├── types/                        # TypeScript types/interfaces
└── utils/                        # Pure utility functions
```

### State management — Context API

Active contexts: `AuthContext`, `SessionContext`, `CartContext`, `TableContext`, `CheckoutContext`, `ThemeContext`. See [ADR-001](docs/adr/ADR-001-app-router-context-api.md).

Pages are **thin orchestrators** (≤ 200 LOC). Logic lives in custom hooks (`src/hooks/use*.ts`).

### Styling — CSS Modules + design tokens

- Every component has `<Component>.module.css` colocated.
- Colours / spacing / typography come from CSS variables defined in `src/app/globals.css`. **Never** hardcode hex values in module CSS.
- Dark mode via `html[data-theme="dark"]` selector. **Never** `@media (prefers-color-scheme: dark)`. See [ADR-002](docs/adr/ADR-002-css-modules-and-tokens.md).

### Internationalisation — i18next, 9 locales

Locales: `en`, `de`, `tr`, `it`, `ar`, `fr`, `es`, `ru`, `zh`. Every UI string MUST be in `src/locales/<locale>.json`. **Never hardcode UI text** in components.

**Locale parity** is required: every key added to `en.json` must be added to all 8 other locales in the same MR. See [ADR-003](docs/adr/ADR-003-i18next-locale-parity.md).

### Forms — Zod + react-hook-form

Schema-first: define a Zod schema, derive the type with `z.infer`, wire to `react-hook-form` via `@hookform/resolvers/zod`. See [ADR-004](docs/adr/ADR-004-zod-form-validation.md).

### Design-system primitives (mandatory wrappers)

| Component | Use for |
|---|---|
| `BaseModal` | every modal/dialog overlay (no raw `<dialog>`, no headlessui Dialog used directly) |
| `FormField` | every label+input+error grouping |
| `StatusBadge` | every status pill/badge |

See [ADR-005](docs/adr/ADR-005-design-system-primitives.md).

---

## §4 — File length limits

Enforced by [scripts/check-file-length.sh](scripts/check-file-length.sh) (pre-commit + CI `file_length` job, blocking).

| File type | Max LOC | Action if exceeded |
|---|---|---|
| Page component (`src/app/**/page.tsx`) | 200 | Move logic into a custom hook in `src/hooks/` |
| Modal component (`*Modal.tsx`) | 200 | Split form / preview / actions into sub-components |
| UI component (other `*.tsx` in `src/`) | 250 | Extract sub-components into the same folder |
| Custom hook (`src/hooks/use*.ts`) | 200 | Split by concern (data-fetching vs derived state vs side-effects) |
| Service file (`src/services/**`, `src/lib/**`) | 200 | Split by resource (one service per backend feature) |
| Type / interface file (`src/types/**`) | 150 | Split by domain |
| CSS Module (`*.module.css`) | 200 | Extract sub-component CSS |

Excluded: tests (`*.test.{ts,tsx}`, `*.spec.{ts,tsx}`), Storybook stories (`*.stories.tsx`), Playwright snapshots.

**Existing oversized files** are baselined in [scripts/file-length-baseline.txt](scripts/file-length-baseline.txt) (153 entries — set at the current honest floor; ratchet down as the refactor track lands). New violations block the gate.

**Per-file opt-out** (rare; needs reviewer sign-off): add `// FILE_LENGTH_EXEMPT: <reason>` within the first 5 lines of the file.

**After a refactor lands** that brings a baselined file under its limit:
```bash
bash scripts/check-file-length.sh --regen-baseline
```
Commit the updated `scripts/file-length-baseline.txt` in the same MR.

---

## §5 — Frontend rules (hard)

1. **Pages are orchestrators** — page logic lives in a custom hook; the page component reads from the hook and renders. Max 200 LOC per page.
2. **Modals use `BaseModal`** wrapper. Filename suffix is `Modal` (not `Dialog`).
3. **Forms use `FormField`** for label+input+error pattern.
4. **Status display uses `StatusBadge`** — never inline status pills.
5. **No inline hex colours** in `*.tsx` or `*.module.css` — use CSS variables from `src/app/globals.css`. Dynamically computed colours (e.g. user avatar bg from hash) are the only exception.
6. **CSS Modules required** — no inline `style={{}}` except for dynamic computed values (positions, dimensions from props).
7. **Dark mode** via `html[data-theme="dark"]` — **never** `@media (prefers-color-scheme: dark)`.
8. **No `: any`** in TypeScript — use `unknown` with type guards. (ESLint rule currently disabled at the config level; will be flipped in Sprint 2 — until then, code review enforces.)
9. **API calls** go through `src/lib/apiClient.ts`, organised by resource in `src/services/<resource>Service.ts`.
10. **Component exports**: `export default function ComponentName(...)` (not arrow functions assigned to consts).
11. **No hardcoded UI text** — every string in `src/locales/*.json`. Locale parity required across all 9 locales.
12. **No hardcoded `process.env.NEXT_PUBLIC_*`** literals scattered across components — read once in `src/lib/config.ts`, export typed constants.
13. **No hardcoded URLs** — backend URL via `NEXT_PUBLIC_API_URL`, image base via `NEXT_PUBLIC_IMAGE_BASE_URL`, etc.
14. **Cross-feature imports** (`src/features/X/` reaching into `src/features/Y/`) are forbidden. Shared code goes in `src/components/` or `src/lib/`.

---

## §6 — Pre-implementation verification (REQUIRED for non-trivial work)

> Output this checklist BEFORE writing any implementation code. Skipping = restart the task.
> "Non-trivial" = anything beyond a one-line typo / comment fix.

### 1. Backend contract verification (any change consuming a backend API)
For each backend endpoint or DTO referenced, name the source of truth:
- **Backend file**: `backend/RestaurantSystem.Api/Features/<X>/Dtos/<Y>Dto.cs` (or controller signature)
- **Frontend mirror**: `src/services/types/<Y>.ts` (or `src/types/<Y>.ts`)
- Confirm field names + types match. Flag any mismatch as "needs backend MR" before writing frontend code.

### 2. Sibling conventions
List 2–3 sibling files in the directory you're adding to. Note their structure (default export, hook usage, CSS Module naming). Confirm your new file matches.

### 3. Acceptance criteria audit
Quote the relevant criteria from the sprint task / issue. Mark each:
- **Covered fully** (this MR closes it)
- **Partial** (note what's missing, link follow-up)
- **Out of scope** (note where it'll land)

### 4. i18n key audit (any UI string change)
- List every i18n key added or modified.
- Confirm parity: `en.json` ↔ `de.json` ↔ `tr.json` ↔ `it.json` ↔ `ar.json` ↔ `fr.json` ↔ `es.json` ↔ `ru.json` ↔ `zh.json`.
- For RTL locales (`ar`), confirm any layout changes still work (e.g. flex-direction in mirrored components).

### 5. Existing references
Grep for the component / hook / type you're adding or modifying. List every callsite. Confirm each still works after your change OR mark for update in this MR.

### 6. Cross-cutting check
- Does this affect the `backend` repo (DTO contract changes)?
- Does this affect the `printer-app` repo (DTO contracts that may have to mirror backend changes)?
- If yes, flag as "breaking" / "additive" in the MR description.

---

## §7 — Quality gates

| Enforcement | Gate | When | What | Source of truth |
|---|---|---|---|---|
| **CI-enforced (blocking)** | `npm test` | MR pipeline | Jest unit tests | `.gitlab-ci.yml` (`npm_test` job) |
| **CI-enforced (blocking)** | `npm audit --audit-level=high` | MR pipeline | No high/critical vulnerabilities | `.gitlab-ci.yml` (`npm_audit` job) |
| **CI-enforced (blocking)** | Gitleaks | MR pipeline | No leaked credentials (allowlist via `.gitleaks.toml`) | [.gitleaks.toml](.gitleaks.toml) |
| **CI-enforced (blocking)** | njsscan | MR pipeline | Static security scan for JS | `.gitlab-ci.yml` |
| **CI-enforced (blocking)** | semgrep | MR pipeline | SAST | `.gitlab-ci.yml` |
| **CI-enforced (blocking)** | retire.js | MR pipeline | Outdated-dep CVE scan (replaced by OSV-Scanner in Sprint 4) | `.gitlab-ci.yml` |
| **CI-enforced (blocking)** | Trivy image scan | MR pipeline (`trivy` job, after `build_image`) | Zero HIGH/CRITICAL CVEs in the built image. False-positive exclusions live in `.trivyignore` with written justification. | `.gitlab-ci.yml`, `.trivyignore` |
| **Pre-commit** (blocking on `git commit`) | `pre-commit` hooks | Every commit | trailing-whitespace, EOF, large files, secret scan, no-commit-to-protected | [.pre-commit-config.yaml](.pre-commit-config.yaml) |
| **CI-enforced (blocking)** | `prettier --check` | MR pipeline (`prettier_check` job) **and** pre-commit when staged file matches `^src/.*\.(ts\|tsx\|css\|json\|md)$` | Source is prettier-clean | `.gitlab-ci.yml`, `.pre-commit-config.yaml` |
| **CI-enforced (blocking)** | `tsc --noEmit` | MR pipeline (`typecheck` job) **and** pre-commit when any `.ts`/`.tsx` is staged | Whole-project typecheck passes | `.gitlab-ci.yml`, `.pre-commit-config.yaml` |
| **CI-enforced (blocking)** | `eslint --max-warnings=0` | MR pipeline (`eslint` job) **and** pre-commit when any `.ts`/`.tsx`/`.js`/`.mjs`/`.cjs` is staged | Zero lint warnings (allow-list configured per rule, see `eslint.config.mjs`) | `.gitlab-ci.yml`, `.pre-commit-config.yaml`, `eslint.config.mjs` |
| **CI-enforced (blocking)** | File-length gate | MR pipeline (`file_length` job) **and** pre-commit (per-file when `.ts`/`.tsx`/`.module.css` is staged) | LOC ≤ §4 limit OR file is in `scripts/file-length-baseline.txt` | [scripts/check-file-length.sh](scripts/check-file-length.sh), `.gitlab-ci.yml`, `.pre-commit-config.yaml` |
| **Sprint 1 — manual** (devs run before commit; not yet automated) | `npm run build` | Manual | Next.js build succeeds | `next.config.ts` |

**`prettier --check`, `tsc --noEmit`, and `eslint --max-warnings=0` all automated and blocking as of Sprint 2 / 2.5**. SAST quality gate (SonarCloud) lands in Sprint 3.

### Setup for a new developer
```bash
bash scripts/setup_hooks.sh   # installs pre-commit hooks (one-time)
bash scripts/dev-secrets.sh   # bootstraps .env.local from .env.example (one-time)
bash scripts/dev-up.sh        # health-checks backend, then runs `npm run dev`
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
- Branch off **`develop`**. Open MR to `develop`. After merge to `develop` and test-env validation, `develop` is promoted to `main` for prod.
- Branch naming: `feature/`, `fix/`, `chore/`, `docs/`, `test/`.

### Commit messages

Format: `type(scope): description`. Same convention as backend (see backend `CLAUDE.md` §8 for the type table).

### Merge requests

Every MR uses [.gitlab/merge_request_templates/Default.md](.gitlab/merge_request_templates/Default.md). Required sections: summary, sprint-task link, acceptance-criteria coverage, backend-contract verification + i18n parity (where applicable), standard checklist, test plan, deploy notes.

---

## §9 — AI guardrails (refusal list)

Never auto-edit these files / take these actions without explicit user instruction:

### Hard refusals
- **`next.config.ts` security headers** (CSP, frame-options, HSTS). Changing these can break the production deployment in subtle ways. Treat as a deploy event, not a code change.
- **Major version bumps** of `next`, `react`, `react-dom` in `package.json`. Breaking-changes class — coordinated upgrade only.
- **Playwright golden snapshots** in `tests/**/*-snapshots/` — never hand-edit; only regenerate via explicit `npx playwright test --update-snapshots`.
- **`src/locales/*.json` for languages the agent doesn't read** (typically `ar`, `ru`, `zh`). Adding/removing keys is fine (locale parity), but **rephrasing existing translations** in those locales requires a human translator or explicit user instruction.
- **Branch protection bypass**: never `git commit --no-verify`, `git push --force-with-lease` to `develop`/`main`, `git reset --hard` on `develop`/`main`.

### Cross-repo coordination required
- **Backend DTO contract changes** — affects `src/services/types/` and any consumer. Before modifying a frontend type that mirrors a backend DTO, grep usages and flag the cross-repo coordination in the MR.

### Sensitive-file refusal (matches gitleaks/detect-secrets allowlist)

Never commit:
- `.env.local`, `.env.development.local`, `.env.test.local`, `.env.production.local` — gitignored; if missing, flag, don't fabricate
- `*.pem`, `*.key`, `*.pfx`, `*.p12`, `*.cer`
- Any file matching `*secret*`, `*credentials*`

**Intentionally-tracked env files (do NOT delete; do NOT add secrets to):**
- `.env.example` — template that `scripts/dev-secrets.sh` copies into `.env.local`. `NEXT_PUBLIC_*` defaults + empty placeholders for per-developer credentials (`ADMIN` / `CASHIER` / `CUSTOMER`).
- `.env.production` — public deploy-time defaults for the production build. Only `NEXT_PUBLIC_*` values, **never** secrets. Whitelisted in `.gitignore` via `!.env.production`. If you need to add a runtime secret to production, that's a K8s/ArgoCD config change, not a code change to this file.

Bare `.env` is gitignored — credentials live in `.env.local` (per-developer, gitignored).

---

## §10 — Session workflow

### Starting
1. Read this file (auto-loaded).
2. Read [docs/SPRINT-PLAN.md](docs/SPRINT-PLAN.md) if picking up a refactor task.
3. Read the relevant ADR if working on a load-bearing pattern (state mgmt, styling, i18n, forms, design system).
4. Run `npm run lint && npm run build` — confirm baseline green.
5. Check `git status` — start from clean tree on `develop`.

### During implementation
1. Output the §6 verification block before writing code.
2. After non-trivial changes, run `npm run lint` (catches type / hook / import errors fast).
3. Use design-system primitives: `BaseModal`, `FormField`, `StatusBadge`.
4. Read translation values from `t('key')`, never hardcode strings. Add the key to all 9 locales in the same change.
5. Read env vars via `src/lib/config.ts`, never `process.env.NEXT_PUBLIC_*` scattered.

### Before ending
1. `npm run lint` → 0 errors.
2. `npm run build` → succeeds.
3. `npm test` → all passing.
4. `git status` → only intentional changes staged.
5. Commit with `type(scope):` format.
6. Push to feature branch.
7. Open MR via `glab mr create` (or GitLab UI) — fill template fully, including i18n parity confirmation.
