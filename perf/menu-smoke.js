import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend } from 'k6/metrics';

// ---------------------------------------------------------------------------
// Menu + checkout-entry perf SMOKE (DEV-PHASES W3 "operate", D2 performance).
//
// A smoke, NOT a load test: a few VUs for a short window that verify the public
// customer read path is up and answers within a latency budget. It exercises
// the endpoints a browser hits when a guest opens the menu and enters checkout
// (categories/products/menus/specials + is-open/enabled-order-types) — all
// [AllowAnonymous] GETs, so nothing is authenticated and nothing is mutated.
//
// Deliberately excluded: order submission (POST /api/basket, /api/orders) — a
// mutating, authenticated path that would create real rows and needs a seeded
// user + cart. Availability/latency of the read path is the actionable D2
// signal here; order-flow correctness is covered by the frontend Playwright
// E2E, not by a perf probe.
//
// Target is BASE_URL (default: Sofra staging). Point it elsewhere for a tenant:
//   BASE_URL=https://www.rumirestaurant.ch k6 run menu-smoke.js
// ---------------------------------------------------------------------------

// Trailing slashes stripped without a regex (a `/+$` pattern trips ReDoS
// linters) so `${BASE_URL}${path}` never doubles the slash.
let baseUrl = __ENV.BASE_URL || 'https://staging.fooderist.com';
while (baseUrl.endsWith('/')) baseUrl = baseUrl.slice(0, -1);
const BASE_URL = baseUrl;

// Per-surface latency trends so a red run points at the slow group, not just
// "something is slow". `true` => report in ms with p(95)/p(99) in the summary.
const menuBrowse = new Trend('menu_browse_duration', true);
const checkoutEntry = new Trend('checkout_entry_duration', true);

export const options = {
  scenarios: {
    smoke: {
      executor: 'constant-vus',
      vus: 3,
      duration: '30s',
    },
  },
  // Generous FIRST budgets, to be ratcheted down after a bed-in period
  // (DEV-PHASES-PLAN §8: "generous first budgets, tighten by ratchet").
  // Sequential single probes measured each endpoint at ~85-215ms; under k6 with
  // per-VU connection reuse a warm smoke sits ~35ms p95. A GitHub runner adds
  // cross-region RTT + TLS, so the ceilings below carry real headroom.
  // A breach fails the run = alert (not a broken pipeline) — the owner triages.
  thresholds: {
    http_req_failed: ['rate<0.01'], // <1% of requests error / return non-2xx
    // Gate the check() assertions too: in k6 only thresholds affect the exit
    // code, so without this a 200 with an empty/garbage body would stay green.
    checks: ['rate>0.99'],
    http_req_duration: ['p(95)<1200', 'p(99)<2500'],
    menu_browse_duration: ['p(95)<1500'],
    checkout_entry_duration: ['p(95)<1200'],
  },
};

// GET a public endpoint, assert it is healthy, and return its request duration.
function probe(path, name) {
  const res = http.get(`${BASE_URL}${path}`, { tags: { endpoint: name } });
  check(res, {
    [`${name}: status 200`]: (r) => r.status === 200,
    [`${name}: non-empty body`]: (r) => r.body != null && r.body.length > 0,
  });
  return res.timings.duration;
}

export default function menuSmoke() {
  group('menu browse', () => {
    menuBrowse.add(probe('/api/categories', 'categories'));
    menuBrowse.add(probe('/api/products', 'products'));
    menuBrowse.add(probe('/api/menus', 'menus'));
    menuBrowse.add(probe('/api/products/specials', 'specials'));
  });

  group('checkout entry', () => {
    checkoutEntry.add(probe('/api/workinghours/today', 'workinghours'));
    checkoutEntry.add(probe('/api/ordertypeconfiguration/enabled', 'order-types'));
  });

  sleep(1); // pace each virtual user ~1 iteration/sec — keep staging load light
}
