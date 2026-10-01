# ADR-005: central channel gateway, starting with Uber sandbox ingress

Status: accepted for sandbox ingress and private operator testing; production order routing remains gated.

## Context

One marketplace integrator app serves many separately deployed tenant backends. Uber requires one primary
webhook URL and signs original request bytes with its app client secret. The Next.js control plane must not
hold raw orders or provider secrets, and live tenant APIs must not become a cross-tenant webhook router.

## Decision

Keep a separate .NET API, pure Domain and Infrastructure project in the backend repository. It builds a
separate container; the existing tenant API never references these projects. The first deployment is a
dedicated **sandbox-only** service and PostgreSQL database on the staging box. No production key or tenant
mapping is configured. Future provider adapters extend this seam, rather than changing tenant APIs to ingest
untrusted provider payloads.

`POST /api/webhooks/uber-eats` verifies `X-Uber-Signature` with HMAC-SHA256 over untouched bytes and a
constant-time comparison. It requires the testing app's signing key and a configured test-store
allowlist. A provisioning envelope's declared app identity must match that app. Settings are IOptions/env,
and size/rate limits bound unauthenticated traffic. The controller dispatches a command through a custom
mediator; no MediatR dependency is introduced.

Uber's event reference documents `X-Environment` as a delivery infrastructure label. That header is not
covered by the body HMAC and cannot establish application or store identity. A real test order's signature
passed while the previous exact `sandbox` label check rejected delivery. Accept notifications based on the
testing app's key and exact-store allowlist, independently of that unsigned label; outbound requests remain
restricted to sandbox domains. Logs classify the label using only fixed sandbox/production/absent/other
categories, never arbitrary headers, payloads, URLs or identifiers. Forged signatures and other stores remain
rejected for every transport label. No production credentials or tenant mapping are configured.

Persist a minimal notification receipt using parameterized PostgreSQL statements. Its primary key is the
app's client ID plus provider event ID. This database currently contains Uber receipts exclusively; adding
another provider requires a provider namespace migration before its adapter is enabled. Insert uses
`ON CONFLICT DO NOTHING` and verifies the stored raw-body SHA256 hash on duplicates. A changed body with
the same key returns 409. Events may arrive out of order; their integer provider timestamp is retained
without assuming a unit (the provider documentation contains both seconds and nanosecond examples).

Only after the row commits does the API return an empty 200. Database failure/throttling returns 503 to
request provider retries. Missing secrets return 503; invalid signatures 401; unapproved stores 403;
malformed metadata 400; oversized requests 413. Neither a 200 nor `Received` claims that an order was
accepted, fetched or injected. The private console retrieves referenced orders separately.

The receiver stores **no raw payloads, resource URLs or customer/order details**. Later processing must
re-fetch canonical provider data with an allowlisted base URL and mapped resource IDs, and introduce the
approved encryption/retention policy before storing payloads. Public health is dependency-free liveness;
database readiness is proven by an authorized synthetic signed receipt and database readback.

Schema source: `RestaurantSystem.Channels.Infrastructure/Migrations/001_webhook_inbox.sql` (new database):
`client_id`, `event_id`, `event_type`, `resource_id` = varchar(128); `store_id` = uuid; `event_time` = bigint;
`body_hash` = char(64); `received_at` = timestamptz; `state` = varchar(24), initially Received. Apply once
explicitly before the API; no startup migrations. No existing EF migration or tenant schema is changed.

## Consequences and scope

- The test store can be linked to an actual signed, durable webhook while the full connector is built.
- Production scope/key support, tenant routing, automatic workers/reconciliation, tenant catalogue publishing,
  customer-data retention and staff/printer contracts require subsequent slices. No live merchant onboarding
  or production verification is requested by this receiver.
- Sandbox receipts remain diagnostic metadata in the isolated DB. Establish the supported retention,
  encrypted payload storage, backup/restore and alerting policy before advancing to order processing.
- Place test orders only with the private console open and an operator ready to accept/deny within Uber's
  acceptance window. A notification acknowledgment does not accept the order.

## Private sandbox connection and testing

An opt-in console at `/console/` serves the one configured sandbox store. It has no tenant authentication,
entitlement or order ingestion contract. The owner access key is represented in configuration only by its
SHA256 hash. Successful login issues a random opaque, HttpOnly/Secure/SameSite=Lax `__Host-` cookie; only
its hash and a bounded expiry are stored. Origin validation protects every mutation including login/logout.
All console responses are no-store, no-referrer and noindex, with a CSP excluding inline scripts and framing.
Login/console traffic is separately rate limited. Disabled is the default; production Uber hosts are rejected.

OAuth requests use `eats.pos_provisioning`, PKCE S256 and an expiring random state bound to that session and
store UUID. An atomic state claim prevents callback replay/concurrent exchange. Discover the merchant's
stores, require the exact allowlisted UUID, then nominate order-manager access while retaining tablet acceptance, using a JSON request body.
The merchant token exists only during discovery/activation. Callback redirects contain no code or token.
Provider HTTP redirects are disabled; response sizes and whole-request deadlines are bounded.

App tokens request only `eats.store eats.order` (the latter also authorizes v2 order reads). Cache until
five minutes before expiry, coalesce concurrent refreshes, and encrypt with AES-256-GCM using a separate
configuration key and app/store/token-kind associated data. Never return a token to browser code or log
provider payloads. Schema source is additive `002_sandbox_console.sql`: session/state hashes, encrypted
PKCE verifiers and app tokens, and per-order action metadata. Migration 001 and all tenant schemas remain
unchanged. Apply 002 explicitly and grant CRUD on only its four tables to the API role; receipt permissions
remain INSERT/SELECT. Back up the isolated DB and key separately before deployment/rotation.

Menu upload is an explicit replacement of a versioned two-item sandbox fixture, previewed with its test
hours and EUR prices. Readback must preserve published content and stable IDs before a publish is
reported verified or order-manager testing enabled. Uber's actual sandbox GET normalizes an empty
modifier-group array to null and omits `type: ITEM` on category entities. Only those two equivalences are
allowed: an explicit different type, changed prices/tax/hours/content/IDs, missing fields and nonempty
modifier groups still fail. An independently captured provider readback and hostile mutations cover this
boundary. `GET /api/sandbox/uber/verification` verifies the current menu without issuing another upload.
Enabling/resuming testing uses another session-bound OAuth intent; its callback first reactivates with
tablet acceptance, verifies the menu, then uses the merchant token to turn off tablet acceptance.
App-token PATCH is restricted to pausing/relinquishing management. Provider-added defaults are permitted. Configuration
readback distinguishes linked, enabled, pending promotion and actual order-manager identity. Pause testing
is a separate action. No tenant working hours, menu, tax settings or publishing revision is inferred.

Order reads require an authenticated stored notification for the configured app/store and a canonical
GUID. Build the fixed v2 API path; never follow resource_href. Require the returned order/store IDs to
match. Display the complete order including customer/item instructions without persisting customer data.
Accept/deny requires explicit instruction review, a reason, a fresh CREATED state and confirmed order-manager
configuration. An atomic unique app/store/order action claim commits before the provider POST. Record
acknowledged, definitively rejected or uncertain outcomes; uncertain calls cannot be retried automatically.
Only a matching ACCEPTED/DENIED readback reconciles an uncertain decision. Known 4xx rejection permits
manual retry after another fresh order read. Restart preserves these guards. Order JSON remains ephemeral.

This console proves provider behavior; it does not create tenant POS orders or print kitchen tickets.
Sandbox-only operator testing is the next acceptance gate before tenant routing is designed and released.
The deploy runbook defines backup, rollback and key rotation for this environment.

Static-analysis classification: the hand-authored SQL is PostgreSQL 16, verified by applying it in gateway
integration tests. Oracle PL/SQL suggestions to replace CHAR/VARCHAR with VARCHAR2 are inapplicable and are
reviewed as false positives, not implemented. The custom CQRS query marker's generic result type binds its
handler/dispatcher constraints at compile time, matching the existing tenant mediator convention.

Sources: [Uber webhooks](https://developer.uber.com/docs/eats/guides/webhooks),
[order notification](https://developer.uber.com/docs/eats/references/api/webhooks.orders-notification),
[store provisioning](https://developer.uber.com/docs/eats/references/api/webhooks/store-provisioned).
