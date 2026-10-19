# Customer mobile basket contract

Native routes require a canonical lowercase, nonempty UUID in `X-Session-Id`. A customer JWT is optional;
when supplied it must authenticate as Customer. Failed authentication never falls back to a guest.
Staff and machine credentials are refused. Guests address only their unowned session; customers
address only their own basket. Responses use the existing `ApiResponse<BasketDto>` envelope.

| Route | Purpose |
| --- | --- |
| `GET /api/customer-mobile/basket` | Read the current owned basket; never creates a basket or acknowledges an operation |
| `POST /api/customer-mobile/basket/items` | Add with a mandatory nonempty `operationId` and existing add fields |
| `POST /api/customer-mobile/basket/items/{basketItemId}/operations` | Recoverable absolute quantity/instruction update or root removal |

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

## Recovery and atomicity

Operation advisory locks serialize replay. Shared owner locks cover native and legacy basket/channel
service writes, including login merge; customer locks span devices. Merge locks both guest and customer
owners in deterministic order. Read-only service calls take no write lock.
The basket effect and receipt commit in one transaction, including the existing basket service's saves.
A failure before commit rolls both back. Receipts retain only UUIDs/hashes/time, survive basket cleanup
and cannot be removed by the guarded historical downgrade. No new migration accompanies root edits or write coordination.

Exact owned replay returns the current original basket without repeating the effect. Changed payloads
or route IDs conflict; a different principal/session cannot recover the receipt. A deleted/replaced
original basket returns 409 and is never recreated by replay. An intervening accepted operation or web
edit can change the returned current basket; replay is not an immutable basket snapshot.

Clients persist the exact request, owner, session and operation identity securely before dispatch.
An ordinary read, error response, timeout or local navigation is not permission to discard an unresolved
operation. Recovery is explicit. This contract does not cancel/seal an unapplied request or permit
order/payment retries; those require their own durable server proof.
