# Customer mobile basket contract

Native routes require a canonical lowercase, nonempty UUID in `X-Session-Id`. A customer JWT is optional;
when supplied it must authenticate as Customer. Failed authentication never falls back to a guest.
Staff and machine credentials are refused. Guests address only their unowned session; customers
address only their own basket. Reads/effects use `ApiResponse<BasketDto>`; cancellation uses the resolution envelope below.

| Route | Purpose |
| --- | --- |
| `GET /api/customer-mobile/basket` | Read the current owned basket; never creates a basket or acknowledges an operation |
| `POST /api/customer-mobile/basket/items` | Add with a mandatory nonempty `operationId` and existing add fields |
| `POST /api/customer-mobile/basket/items/{basketItemId}/operations` | Recoverable absolute quantity/instruction update or root removal |
| `POST /api/customer-mobile/basket/items/cancel` | Seal an unresolved original add, or prove it was already applied |
| `POST /api/customer-mobile/basket/items/{basketItemId}/operations/cancel` | Seal an unresolved original root edit/removal, or prove it was already applied |
| `GET /api/customer-mobile/basket/channel-preview?orderType=Takeaway` | Read owned basket revision and root-level conflicts without creating or changing a basket |
| `POST /api/customer-mobile/basket/channel` | Durably switch to an enabled channel using an exact reviewed revision and confirmed conflict set |
| `POST /api/customer-mobile/basket/channel/cancel` | Seal the exact original channel request, or prove it was already applied |

New reads/effects require native ordering opt-in. Existing accepted operations remain recoverable after
withdrawal. All native writes share the configured add ingress budget with the legacy add endpoint;
rotating sessions or route names does not reset an IP's budget. Native clients must not fall back to
legacy routes when a dedicated route is unavailable.

## Root-item mutation request

- `operationId`: nonempty UUID, reused only for this exact logical request and root route.
- `expectedBasketId`: nonempty UUID from the reviewed server basket.
- `expectedPurchaseFingerprint`: the server's 64-character hexadecimal purchase digest.
- `action`: required string enum `Update` or `Remove`.
- `quantity`: required for Update, positive and at most `Basket:MaxQuantityPerItem`.
- `specialInstructions`: nullable replacement, at most 500 characters. An absolute quantity edit must
  also preserve the existing instructions if the customer did not change them.

Remove requires quantity and instructions to be absent/null. It removes the root and its descendants;
children cannot be independently updated/deleted. Prices, totals, owner IDs and staff flags are never taken from the request.
The reviewed basket ID/digest must still match inside the native operation transaction. A missing root
returns 404; a changed basket returns 409. Neither failure creates an acceptance receipt.

## Reviewed channel switches

Preview returns `basketId`, `purchaseFingerprint`, the requested `orderType`, and `conflicts` using the
same whole-line/category/side scan as web switching. A missing basket has nil ID, empty digest and no
conflicts; preview never creates a basket or receipt. Enabled order types remain available through
`GET /api/OrderTypeConfiguration/enabled` and are checked again on a new native switch.

Channel requests require `operationId`, `expectedBasketId`, `expectedPurchaseFingerprint`, `orderType`
and an explicit `confirmedConflictItemIds` array. Empty basket identity/digest is allowed only when
there is no current owned basket. Otherwise the reviewed ID and purchase digest must match. The
current root conflict set must exactly equal the confirmed IDs at the authoritative removal scan;
new, removed or changed conflicts cause 409 without dropping any line or creating a receipt.
Children and sides stay root-granular. A confirmed root and all descendants are removed together.

Native switches use the same effect/receipt transaction, owner lock, recovery and cancellation rules
as item mutations, with a distinct channel payload kind. Existing web switch contracts are unchanged.
No independent native clear-channel action or new database schema is introduced. Replay and sealing
remain possible after channel availability or native ordering is withdrawn.

## Recovery and atomicity

Operation advisory locks serialize replay. Shared owner locks cover native and legacy basket/channel
service writes, including login merge; customer locks span devices. Merge locks both guest and customer
owners in deterministic order. Read-only service calls take no write lock.
The basket effect and receipt commit in one transaction, including the existing basket service's saves.
A failure before commit rolls both back. Receipts retain only UUIDs/hashes/decision time and cancellation state, survive basket cleanup
and cannot be removed by the guarded historical downgrade. No new migration accompanies root edits or write coordination.

Exact owned replay returns the current original basket without repeating the effect. Changed payloads
or route IDs conflict; a different principal/session cannot recover the receipt. A deleted/replaced
original basket returns 409 and is never recreated by replay. An intervening accepted operation or web
edit can change the returned current basket; replay is not an immutable basket snapshot.

Clients persist the exact request, owner, session and operation identity securely before dispatch.
An ordinary read, error response, timeout or local navigation is not permission to discard an unresolved
operation. Recovery and cancellation are explicit. Order/payment retries require separate durable
server proof; basket receipts cannot authorize them.

## Explicit cancellation and sealing

Send the **original execution body unchanged** to its matching `/cancel` route, with the same session,
customer and operation ID. The server uses identical request normalization/digests for execution and
cancellation. Both acquire the same operation/owner locks; a response is sent only after commit.

`ApiResponse<CustomerBasketOperationResolutionDto>` contains:

- `operationId`: the exact original UUID.
- `outcome`: `Cancelled` or `Applied`.
- `basket`: current owned original basket when available for Applied; nullable/omitted otherwise.

An unseen operation commits a durable Cancelled receipt without creating/updating a basket. The
original execution then conflicts forever, including when it arrives after cancellation or through
an equivalent legacy add URL. Repeat cancellation returns the same decision. An existing applied
receipt returns Applied; it never undoes that effect. If its basket was erased/replaced, Applied
still proves the original decision and returns no basket, without resurrecting it or substituting a
new basket. Different payloads/routes conflict and a different principal/session receives no proof.

Cancellation remains available after native ordering is withdrawn and does not revalidate current
availability/quantity rules. A previously refused request also needs a seal before it can be safely
discarded. Staff and failed present authentication remain refused. Cancellation shares the write
rate budget, so creating decisions cannot bypass the existing ingress limit. No raw request is stored.

`20261019045735_AddCustomerBasketCancellation` adds nonnullable `is_cancelled` with default false,
preserving older accepted receipts as Applied. Cancelled receipts keep a nil basket ID as an additional
compatibility barrier for older native executors, which refuse a replay without the original basket.
Its guarded downgrade locks the table and refuses to erase any receipt history, including a concurrent
commit. Update `TestDatabaseCluster.CurrentSchemaMigration` when extending this schema. Source tests
and migration generation do not constitute production application or deployed API readback.
