# Recoverable customer basket adds

`POST /api/basket/items` accepts an optional `operationId` UUID. Legacy requests without it retain
the existing add semantics. The operation rail requires a non-empty lower-case canonical UUID
`X-Session-Id`, a guest or customer principal, and an enabled mobile ordering capability for new
operations. Other roles and machine tokens cannot use it. A present but rejected Authorization
header returns HTTP 401; it cannot silently downgrade a customer operation into a guest add.

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
