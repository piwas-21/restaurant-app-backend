# Recoverable customer basket adds

`POST /api/customer-mobile/basket/items` requires a non-empty `operationId` UUID. Native clients
must use this dedicated route: older deployments return 404 instead of silently ignoring the retry
identity. `POST /api/basket/items` also accepts the optional field for compatible existing callers. Legacy requests without it retain
the existing add semantics. The operation rail requires a non-empty lower-case canonical UUID
`X-Session-Id`, a guest or customer principal, and an enabled mobile ordering capability for new
operations. Other roles and machine tokens cannot use it. A present but rejected Authorization
header returns HTTP 401; it cannot silently downgrade a customer operation into a guest add.

## Native basket reads

`GET /api/customer-mobile/basket` requires the same canonical session, customer-or-guest access
and enabled native ordering rollout. It rejects a present failed Authorization header before lookup,
using the same access guard as durable adds. Staff and API tokens are forbidden. It delegates the
existing owned read: customers are matched by customer ID, guests by an unowned exact session.
Reads create no basket or receipt and do not acknowledge a pending add.
The shared read logs the operation without recording its raw basket-session capability.

A missing basket returns the existing nil-ID, null-user, zero-total, empty-items envelope with the
request session. Clients must use the native URL: the legacy anonymous read can return that same
envelope after rejecting a customer JWT, so it cannot distinguish expired authentication from a
genuinely missing customer basket. Older deployments return 404 on the native route; do not fall back.
This additive route requires deployment but no further schema or DTO change. Existing receipt replay
remains separately available after rollout withdrawal; a fresh native read is rollout gated.

Persist the operation ID, exact request and original environment/tenant/principal/session before
sending. Reuse that identity only for a retry of the same logical add. A deliberate additional add
needs a new operation. This contract does not authorize automatic order or payment retries.

## Transaction and recovery

The operation-ID lock and owner lock cover the existing basket service's saves and the durable
receipt in one PostgreSQL transaction. Concurrent operation-ID adds for one customer basket are
serialized, including different device sessions for the same authenticated customer. Other basket
writers (legacy adds, edits, removals, channel changes and login merges) do not use these locks.

Receipts store only operation ID, owner hash, canonical request hash, basket ID and application time.
Object property order is canonicalized; array order remains significant. No request body, session,
credential or customer contact fields are copied into the receipt. Receipt ownership binds both
principal and exact session; switching from guest to customer or changing the session cannot recover
another context's result. The operation namespace is scoped by the tenant's existing database.

| Outcome | Behavior |
| --- | --- |
| First valid submission | Add and receipt commit together; return server-priced basket |
| Same owned operation and request | Return current state of the original basket; do not add again |
| Same operation, different request | HTTP 409; do not silently reinterpret the operation |
| Another owner/session | HTTP 404; do not disclose the receipt or basket |
| Rejected authentication header | HTTP 401 before effects; renew the original customer session |
| Invalid operation/session | HTTP 400 before effects |
| Rollout withdrawn | Existing owned receipt can recover; new operations return HTTP 404 |
| Original basket erased or replaced | HTTP 409; receipt survives and the add cannot resurrect |
| Failure before commit | Basket saves and receipt roll back together |

The replay response is current basket state, not a historical price snapshot. Refresh the basket
after conflicting edits. A basket may still exist with no items after it is cleared; replay leaves
it empty. Cleanup has no foreign-key cascade into receipts, and this change adds no retention job
or retention-window change. Receipt erasure requires a separate design that preserves replay safety.

## Verification and release

Integration fixtures use disposable PostgreSQL and exercise replay, concurrency, changed payloads,
principal/session isolation, capability withdrawal, rollback after real basket saves and cleanup.
This source needs its additive migration and backend deployment before a dependent native release.
Generated production OpenAPI, deployment readback and installed-app recovery remain separate evidence.
Ordering/payment idempotency, full native basket UI and process-death acceptance remain required.

Rollback preserves retry identity: once a receipt exists, the migration refuses downgrade under
an exclusive table lock, including when a concurrent receipt commits while rollback waits. Disable
native ordering and roll back the application while retaining this additive schema. An empty table
can still be rolled back. Historical migration tests restore the current schema before lane reset.

Both add routes share one fixed-window IP budget, configured by
`BasketAdds:AttemptsPerIpPerMinute` (default 600, allowed 1–6000). It covers legacy adds and durable
operations so an alternate route or rotated session ID cannot bypass it. There is no request
queue; exhaustion returns HTTP 429 with Retry-After before basket/receipt effects. The client
retains a pending operation and requires an explicit recovery action. Other IPs have independent
budgets. This is ingress protection, separate from session/principal authorization.
