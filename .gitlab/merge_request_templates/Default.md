<!--
  Default MR template — RUMI Frontend
  See CLAUDE.md §6 (pre-implementation verification) and §8 (git workflow).
  Delete sections that don't apply (e.g. Backend-Contract Verification for non-API changes).
-->

## Summary
<!-- 1–3 bullets describing what this MR does and why. -->
- ...

## Sprint task / issue
<!-- Link the sprint task (docs/SPRINT-PLAN.md task ID) or GitLab issue number. -->
- Closes #
- Sprint task:

## Type
- [ ] `feat` — new user-visible feature
- [ ] `fix` — bug fix
- [ ] `refactor` — no behaviour change
- [ ] `chore` — build / CI / dependencies / config
- [ ] `docs` — documentation only
- [ ] `test` — tests only
- [ ] `perf` — performance / bundle-size

## Acceptance criteria coverage
<!--
  For each acceptance criterion in the linked issue / sprint task, state coverage.
  Delete this section for chore/docs MRs with no acceptance criteria.
-->

| Criterion | Status | Notes |
|---|---|---|
| <criterion 1> | Covered / Partial / Out of scope | <follow-up issue # if partial> |

## Backend contract verification
<!--
  Required if this MR touches: API service files, type files mirroring backend DTOs, or any data shape consumed from the backend.
  Delete the whole section if not applicable.
-->

**Backend DTOs consumed:**
- `backend/RestaurantSystem.Api/Features/<X>/Dtos/<Y>Dto.cs` — fields used: ...

**Frontend mirror types:**
- `src/services/types/<Y>.ts` (or `src/types/<Y>.ts`)

**Verified field-name + type parity:** Yes / No (if no, link the backend MR that needs to land first)

## i18n parity (any UI string change)
<!-- Required if you added or modified any i18n key. Delete if no string changes. -->

**Keys added / modified:**
- `<key>` — purpose: ...

**Locale parity confirmed across:**
- [ ] `en.json`
- [ ] `de.json`
- [ ] `tr.json`
- [ ] `it.json`
- [ ] `ar.json` (RTL — also confirmed layout still works)
- [ ] `fr.json`
- [ ] `es.json`
- [ ] `ru.json`
- [ ] `zh.json`

## Standard checklist
- [ ] `npm run lint` — 0 errors
- [ ] `npm run build` — succeeds
- [ ] `npm test` — all unit tests pass
- [ ] No hardcoded UI strings (all in `src/locales/*.json`)
- [ ] No inline hex colours (use CSS variables from `globals.css`)
- [ ] No `: any` (use `unknown` + type guards)
- [ ] Modals use `BaseModal`, forms use `FormField`, status pills use `StatusBadge`
- [ ] Dark mode uses `html[data-theme="dark"]` (NOT `@media (prefers-color-scheme: dark)`)
- [ ] No raw `process.env.NEXT_PUBLIC_*` scattered — read via `src/lib/config.ts`
- [ ] Sibling file conventions matched (default export, hook usage, CSS Module naming)
- [ ] Pre-commit hooks pass locally (`pre-commit run --all-files`)
- [ ] Branch is off `develop`; MR targets `develop`

## Test plan
<!-- Manual testing steps; specific scenarios to verify. -->
- [ ] ...

## Screenshots
<!-- For UI changes: before/after for light AND dark mode, mobile AND desktop. -->

## Deploy notes
<!-- Anything ops needs to know: new env vars, breaking config, downtime risk. -->
- New env vars required: <none / list>
- Build-time secrets required: <none / list>
- Runtime risk: <none / requires backend MR # / requires staging validation>
- Rollback procedure: <standard / special>
