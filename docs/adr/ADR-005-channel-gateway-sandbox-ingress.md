# ADR-005: central channel gateway, starting with Uber sandbox ingress

Status: proposed; production order routing remains gated.

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
constant-time comparison. It requires `X-Environment: sandbox`, a configured app, and a configured test-store
allowlist. A provisioning envelope's declared app identity must match that app. Settings are IOptions/env,
and size/rate limits bound unauthenticated traffic. The controller dispatches a command through a custom
mediator; no MediatR dependency is introduced.

Persist a minimal notification receipt using parameterized PostgreSQL statements. Its primary key is the
app's client ID plus provider event ID. This database currently contains Uber receipts exclusively; adding
another provider requires a provider namespace migration before its adapter is enabled. Insert uses
`ON CONFLICT DO NOTHING` and verifies the stored raw-body SHA256 hash on duplicates. A changed body with
the same key returns 409. Events may arrive out of order; their integer provider timestamp is retained
without assuming a unit (the provider documentation contains both seconds and nanosecond examples).

Only after the row commits does the API return an empty 200. Database failure/throttling returns 503 to
request provider retries. Missing secrets return 503; invalid signatures 401; wrong environment/store 403;
malformed metadata 400; oversized requests 413. Neither a 200 nor `Received` claims that an order was
accepted, fetched or injected. There is no order processor in this slice.

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
- Production scope/key support, tenant routing, workers/reconciliation, menu publishing, order accept/deny,
  customer-data retention and staff/printer contracts require subsequent slices. No live merchant onboarding
  or production verification is requested by this receiver.
- Sandbox receipts remain diagnostic metadata in the isolated DB. Establish the supported retention,
  encrypted payload storage, backup/restore and alerting policy before advancing to order processing.
- Do not place a test order until order accept/deny is implemented; acknowledging notification alone does
  not prevent Uber's order-acceptance timeout.

Sources: [Uber webhooks](https://developer.uber.com/docs/eats/guides/webhooks),
[order notification](https://developer.uber.com/docs/eats/references/api/webhooks.orders-notification),
[store provisioning](https://developer.uber.com/docs/eats/references/api/webhooks/store-provisioned).
