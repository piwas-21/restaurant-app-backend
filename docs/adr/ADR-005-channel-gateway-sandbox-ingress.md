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

### Tenant source contract

The tenant Domain uses an optional one-to-one `ExternalOrderReference`, independent of the gateway projects.
Migration `20261001174539_AddExternalOrderReference` creates its table with a unique local order link and a
unique provider/store/external-order key. The provider is a stable string; adding an adapter does not require
another tenant enum migration. References retain hashes and monetary evidence, never raw provider bodies.

`Orders.Remove` becomes a soft delete during auditing. The reference relationship uses `ClientNoAction`, so EF
cannot delete the loaded idempotency anchor before that conversion. A physical order purge must explicitly
address the reference under the applicable retention policy; a normal operator deletion cannot free its key.
Reference-only updates touch the principal order's version, audit timestamp and change journal, so staff/printer
delta readers see provider-state changes.

Order DTOs carry one optional `externalOrder` object: provider/display ID, provider state/event time, frozen
currency, merchant amount, nullable reported tax, fulfilment type and sandbox marker. Internal store/order IDs
and payload hashes remain outside this shared customer/staff/printer contract. Ordinary orders omit the object,
preserving the existing printer wire snapshot. A marketplace currency precedes tender/tenant display defaults.

Merchant revenue and consumer checkout totals are different. The observed test meal pays the merchant EUR 5.00,
while checkout includes marketplace fees. Missing provider tax is explicitly null, distinct from reported zero;
it must not be inferred from tenant defaults or a published-menu tax fixture.

### Disabled tenant import foundation

`POST /api/delivery-channels/orders` requires the real API-token scheme, its authentication-method claim and
`channels:orders:write`. Human identities and existing `orders:write` tokens cannot import. Deployment-owned
`DeliveryChannels` options default to disabled and sandbox-only; each enabled binding declares provider,
store ID, tenant currency and sandbox identity. An incoming request cannot select another tenant or declare
itself production/sandbox. The tenant must declare a matching currency; no currency is inferred.

This first contract supports simple mapped items and variations with provider delivery. It rejects unknown
JSON fields, bundles/modifier products, restaurant delivery/cash and totals containing unsupported adjustments.
Required source monetary fields distinguish an omitted amount from an explicit zero. Numeric precision and
text limits match existing order/item/tender storage; instructions are preserved without truncation and printer
control bytes are refused. The gateway must validate canonical provider evidence and its complete mapping
before calling this machine endpoint. A hash supplied by the gateway is evidence, not a replacement for that
validation; no raw provider payload or credential enters the tenant request.

One transaction and a per-provider/store/order advisory lock persist the order, snapshot items, source reference
and externally held tender. Identical concurrent retries return one durable identity; changed normalized content
or reuse after soft deletion returns 409. The fingerprint includes canonical evidence and all normalized fields.
Catalogue prices, discounts, loyalty, local guest identity and email credentials do not alter marketplace data.
The externally collected tender names its provider and currency; it does not claim a bank settlement or local refund.

Imported orders remain PendingApproval with kitchen release held. Ordinary status, cancellation, approval,
counter-edit/release, collection, refund and deletion paths refuse them. Staff action projections mirror these
guards; notes and focus remain operational tools. Source-aware staff/browser and printer rendering use the shared source contract. No gateway forwarding or tenant
deployment activation is enabled by the import foundation. Catalogue revisions/modifiers, gateway decision
execution/reconciliation and deployed end-to-end tests remain required before activation.

### Durable tenant decision outbox

Migration `20261001200651_AddChannelOrderDecision` adds a retained, one-per-order decision with a unique operation
ID and normalized request hash. A signed-in cashier/admin queues accept or deny using the current order version;
machine tokens cannot originate that staff decision. Operation/order advisory locks and the principal row lock
serialize retries and competing decisions. An identical retry returns the existing operation; changed content,
a competing action, a stale version or an order beyond held CREATED returns conflict. Queueing never releases
preparation or issues a provider call. Staff can still read an existing result while integration is paused.

The dedicated machine claim/report endpoints require the same actual API-token scheme and `channels:orders:write`
as import. Claims select only current exact store/currency/sandbox bindings before choosing the oldest eligible
job, so a retained paused-store job cannot starve another enabled store. An opaque two-minute lease and atomic
claim/report serialization prevent concurrent workers from applying different results. Expired/replaced lease
reports conflict; exact reports can replay without repeating local lifecycle effects. Unknown outcomes remain
held and become claimable after a bounded retry delay. The gateway must reconcile canonical provider state
before deciding whether another provider request is safe; a claim alone does not authorize blind POST retries.

Success requires canonical ACCEPTED/FINISHED for accept or DENIED for deny. CANCELED is a failed decision;
stale/future observations and mismatched canonical states are refused. Only confirmed ACCEPTED creates routes,
captures release time and originating staff audit identity, and emits the existing order-created notification
after the transaction commits. FINISHED, DENIED and CANCELED never start preparation. Status history, source
state and observation hash update without changing the imported money, tax evidence or original payload hash.

The API-key printer feed provides explicit source/lifecycle PrintKitchen and PrintReceipt grants without a
human role. It shares those source rules with staff projections; held, canceled and finished orders cannot
become new kitchen tickets. An actual HTTP device-feed test verifies held exclusion and the accepted wire
contract. Provider identifiers and opaque leases remain outside staff/printer DTOs. No gateway dispatcher,
provider reconciliation worker or automatic tenant activation is included in this tenant outbox.

- The test store can be linked to an actual signed, durable webhook while the full connector is built.
- Production scope/key support, tenant routing, automatic workers/reconciliation, tenant catalogue publishing,
  customer-data retention and deployed staff/printer acceptance require subsequent slices. No live merchant onboarding
  or production verification is requested by this receiver.
- Sandbox receipts remain diagnostic metadata in the isolated DB. Establish the supported retention,
  encrypted payload storage, backup/restore and alerting policy before advancing to order processing.
- Place test orders only with the private console open and an operator ready to accept/deny within Uber's
  acceptance window. A notification acknowledgment does not accept the order.

## Disabled gateway tenant-import bridge

An additive `TenantBridge` service remains disabled by default. Enabling it requires the existing sandbox-only
connection, exactly the approved store, an explicit enrollment timestamp, tenant HTTPS origin/API token,
EUR/CHF currency and a reviewed catalogue revision/menu hash with unique provider-item mappings. Nothing in a
webhook selects a tenant URL, token or local product identity. No paying tenant is activated by the code change.

Migration `003_tenant_import_jobs.sql` belongs only to the dedicated gateway database and is applied explicitly.
Jobs are unique by app/store/order; the first tenant and catalogue revision are retained across discovery retries.
Authenticated `orders.notification` receipts within the enrollment window create jobs; opt-in created-order
recovery can also discover an identity from the approved store's authenticated provider list. Malformed resource
IDs, other stores/apps, old receipts and scheduled events are excluded. Opaque leased claims use PostgreSQL
`FOR UPDATE SKIP LOCKED`; every state mutation checks exact client/store/order/tenant and unexpired lease ownership.

Before an HTTP import, the worker retrieves the canonical order through the existing exact-store read path.
The current simple-item contract refuses terminal states, cash/restaurant delivery, modifiers, promotions,
fulfillment corrections and packaging rather than discarding them. The anonymized customer phone access code is
retained as a separate bounded optional source field. A code requires its phone number; malformed codes are refused.
Authenticated staff and cashier receipts label it; kitchen headers omit it. It is operational customer contact data,
subject to the existing order-data privacy boundary, never an OAuth credential or a logging field. Null codes are
omitted from the import fingerprint so pre-field normalized replay remains compatible.
All currency/money/quantity/item identities and customer/item instruction limits are validated without truncation.
`payment.charges.total` is merchant revenue; the consumer checkout total is not used. Missing reported tax remains
null, distinct from zero. Unsupported orders are quarantined for marketplace fallback without a tenant call.
This simple contract does not yet satisfy the full menu/modifier capability or operator exception interface.

The normalized tenant request is encrypted with AES-GCM and app/store/order/tenant/catalogue-bound associated data,
and commits before its first import attempt. No raw provider body, eater UUID, courier profile, address or tax
profile is stored. Exact prepared content survives gateway restart and a lost tenant response; a changed canonical
read cannot replace it. Tenant import itself remains idempotent and held. Fixed deployment HTTPS origin,
Bearer token, no redirects, bounded replies/deadline and a valid durable local order ID gate confirmation.
A successful HTTP status without that identity stays uncertain. Contract conflicts/refusals quarantine the job.

Encrypted requests clear immediately on confirmed import or quarantine. Their maximum pending retention is
seven days, configurable only downward; the active gateway erases expired ciphertext even with forwarding
paused or disabled, retaining minimal identity/hash/result metadata. Cleanup tolerates ingress-only databases
where migration 003 has not yet been applied. Before rolling back to an older gateway without this cleanup,
pause forwarding and clear remaining encrypted requests under the documented retention procedure; an offline
process cannot enforce deletion. Backup/restore procedures must preserve this same lifetime for ciphertext.

The bridge includes opt-in staff decision dispatch and imported-order lifecycle reconciliation. Tenant activation,
catalogue publishing/stock compatibility and deployed staff/printer acceptance remain required before full
integration verification.

## Missing-notification recovery

`TenantBridge:RecoverCreatedOrders` is independently opt-in and requires an enabled, unpaused exact-store bridge.
A separate worker polls `GET /v1/eats/stores/{store_id}/created-orders` using only `eats.store.orders.read`, with a
separate encrypted token cache/purpose. Its default interval is thirty seconds (bounds 5–300), list limit 200
(bounds 1–200). It never accepts, denies or releases an order. Full lists remain recoverable and emit a fixed
backlog warning; an oversized or malformed list is refused.

Migration `005_created_order_recovery.sql` adds provenance and response-hash metadata to import jobs. Polling
stores only UUID, placement/enrollment boundaries, source marker and SHA-256; it never stores a raw response or
customer fields. The entire response is validated before a transaction inserts eligible candidates. Duplicate
and late webhook delivery cannot replace the first tenant, catalogue revision or prepared ciphertext.

Canonical GET still checks the returned UUID and exact store. For polled identities, it also checks the persisted
enrollment boundary before any tenant delivery or decision. Existing signed evidence remains valid without the
new migration; unknown identities are refused. Migration application remains explicit and polling stays disabled
until the dedicated database is upgraded. No new database-role grants, tenant DTOs or printer contracts are needed.

## Disabled tenant-decision dispatcher

`TenantBridge:DispatchDecisions` remains false until the isolated tenant/store import is
verified. The machine lease adds the local UUID `orderId`, joined against all five retained
import identities and `Imported` state before any provider call. Unconfirmed/wrong links remain retryable and held;
paused/disabled dispatch never claims a job. No new database migration is needed.

The dispatcher reads canonical provider state before any POST. Existing durable console
`Pending`/`Unknown`/`Succeeded` actions are never resent after a lost response or restart.
A provider acknowledgement is followed by another GET; only matching `ACCEPTED`/`FINISHED`
for accept or `DENIED` for deny is reported succeeded. `CANCELED` reports failure; unrecognized
or conflicting evidence remains held for subsequent reconciliation. Reports use the opaque
lease and current observation time; stale ownership is rejected by the tenant. A lost tenant
report reclaims and re-reads provider state rather than sending another successful action.

Import/claim/report share one HTTPS-origin, bearer-authenticated transport with redirects
disabled, a 64 KiB reply limit and configured `HttpTimeoutSeconds` (20; bounds 5–30 even
when disabled). Machine lease/report responses validate identity, types and expected state.
Only hashes/state/identities persist, with no canonical customer payload or exception-value
logging. The worker uses the existing bounded poll interval and scope-per-cycle convention.

Forwarding remains deployment-gated. Preparation-time editing and extended menu support remain open. Deployment
must keep dispatch paused while bindings, tokens, catalogue or provider-manager configuration are changed.

## Canonical lifecycle reconciliation and local preparation

Migration `20261002010447_AddExternalCanonicalHash` adds nullable `canonical_hash varchar(64)` to the tenant's
retained reference, separate from its immutable import fingerprint. The scoped machine-only observation endpoint
matches local UUID plus provider/store/external order before any write. Observations reject stale/future timestamps,
conflicting same-time evidence and reversal of accepted/terminal states. Repeated accepted reads preserve local
preparation and never release a held order without the human decision outbox.

`CANCELED`/`DENIED` close the local order; `FINISHED` completes it. All terminal states close kitchen release while
preserving prior release audit, frozen merchant charges, provider-held payment and unknown tax. Pending decisions
are finalized from terminal evidence and their opaque leases invalidated; already completed decision history
remains unchanged. A later stale delivery report cannot reopen preparation. Status events occur after commit;
there is no local customer email or refund.

Gateway migration `004_order_observations.sql` stores only identity/state/hash/time and leased scheduling metadata.
Imported jobs are joined on all retained identities before claim and completion. `SKIP LOCKED`, bounded retry and
least-available ordering keep reconciliation recoverable and fair. Lost tenant replies trigger another canonical
GET; polling stops only after the tenant confirms the terminal state. Paused/disabled forwarding makes no claim.

Released, provider-accepted Uber-delivery orders permit only local `Preparing`/`Ready` transitions, with a required
current order version and the established kitchen role policy. Local preparation adds no guessed provider ETA.
Payment, cancellation, handover and completion retain their dedicated channel restrictions. Frontend preparation
controls must send the additive version field; staff/printer source DTOs retain their existing fields.

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

Tenant decision timing is deployment configuration: `DeliveryChannels:DecisionLeaseSeconds` defaults to 120
(60–300 allowed), `DecisionRetrySeconds` defaults to 30 (10–300), and `DecisionClockToleranceSeconds` defaults
to 30 (0–60). Bounds are validated even while the channel is disabled. Clock tolerance never replaces the
monotonic canonical observation check or lease ownership. Gateway transport deadlines must fit the selected lease.
