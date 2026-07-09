# Perf smoke (k6)

A light [k6](https://k6.io) **smoke** of the public customer read path, run against
**deployed staging** on a schedule (and on demand). Part of DEV-PHASES-PLAN W3
("operate") — the backend's first **D2 performance** signal.

## What it checks

`menu-smoke.js` drives the endpoints a guest's browser hits when it opens the
menu and enters checkout — all `[AllowAnonymous]` GETs, so nothing is
authenticated and **nothing is mutated**:

| Group | Endpoints |
|---|---|
| menu browse | `GET /api/categories`, `/api/products`, `/api/menus`, `/api/products/specials` |
| checkout entry | `GET /api/workinghours/today`, `/api/ordertypeconfiguration/enabled` |

It is a **smoke, not a load test**: 3 VUs for 30s. It answers "is the read path
up and reasonably fast?", not "how much traffic can it take?". Order submission
(`POST /api/basket`, `/api/orders`) is deliberately excluded — a mutating,
authenticated path that would create real rows; order-flow correctness lives in
the frontend Playwright E2E, not a perf probe.

## Thresholds (bed-in budgets — ratchet down)

Set in `menu-smoke.js` `options.thresholds`:

- `http_req_failed: rate<0.01` — under 1% of requests may error / return non-2xx
- `http_req_duration: p(95)<1200ms, p(99)<2500ms`
- per-group trends `menu_browse_duration` / `checkout_entry_duration`

These are **deliberately generous** for a bed-in period (DEV-PHASES-PLAN §8:
"generous first budgets, tighten by ratchet"). A dev-laptop run measured the
read path at ~30ms p95; a GitHub runner adds cross-region RTT + TLS. Once a
stable CI baseline is observed, lower the ceilings here.

## Run it locally

```sh
# install k6 (https://grafana.com/docs/k6/latest/set-up/install-k6/), then:
k6 run perf/menu-smoke.js                              # default: staging
BASE_URL=https://www.rumirestaurant.ch k6 run perf/menu-smoke.js   # any env/tenant
```

## In CI

`.github/workflows/perf-smoke.yml` runs daily (06:30 UTC) and via
`workflow_dispatch` (with an optional `base_url` input). k6 is version-pinned
and SHA256-verified. A **red run is an alert, not a broken pipeline**: k6
breached a threshold — open the run Summary for the report and investigate
(staging down? slow query? deploy regression?). Nothing auto-merges or deploys
off this workflow.
