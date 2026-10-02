# Native order amendments and guest table visits

These additive contracts are guarded by `OrderAmendmentsV1` and `TableGuestVisitsV1`, both false by default. They preserve order and preparation-batch IDs under a `TableServiceSession.Id`. Financial resolution and online account collection require later compatible slices before production activation.

## Staff amendments

Authenticated Server, Cashier or Admin callers use `POST /api/staff/orders/{orderId}/amendments/quote`, then `POST /api/staff/orders/{orderId}/amendments/commit`. `GET /api/staff/orders/{orderId}/amendments` returns history; `GET /api/staff/amendment-operations/{operationId}` reconciles an uncertain commit. Tenant module, actor, custody, preparation state and quoted versions are enforced by the server.

Quote requests carry the expected order/account versions, additions, one-based unit changes, reason and required acknowledgements. Commit carries the quote ID, client operation UUID, those same versions and review acknowledgement. Reuse the original operation after a lost response. A committed result is immutable and route-bound; changing its request or source order is refused. An expired, confirmed unknown operation can be quoted again.

Menu-root `OrderItemDto` responses have nullable `productId` and `menuID`; clients must handle that identity recursively. Responses and replay snapshots omit contact details, guest status tokens and free preparation text. A client can show reviewed preparation text from its current draft memory. Persist only operation identifiers, expected versions, acknowledgement and expiry for reload recovery.

New or replacement dishes are ordinary supplementary preparation batches. A quote has no reserved daily order number; commit allocates its unique number in the same transaction that inserts the supplement. Typed correction tickets identify the removed source scope and reference a released replacement ticket without instructing the kitchen to prepare it twice. Captured tenders remain unchanged; a pending credit/refund/loyalty resolution is explicit and must not be presented as returned money.

## Guest admission and account reads

`POST /api/table-guest-visits/join` accepts `qrCodeData` plus the staff-issued `admissionCode`. It is intentionally reachable without a login: admission proves access to the current open visit. Staff issue the code with `POST /api/table-guest-visits/{serviceSessionId}/admission-code`; that endpoint requires table-service staff permission and the Server/Cashier module.

The join result returns the visit ID, participant capability and expiry. Hold the capability in per-tab session storage; never put it in a URL or analytics. Only its hash is stored by the server. `GET /api/table-guest-visits/{serviceSessionId}/account` uses `X-Table-Participant` and returns a sanitized item/balance view without other guests' identities or free preparation notes. A close revokes credentials. Account reads use a consistent snapshot followed by a fresh authorization check, so a visit closing during assembly does not expose the former party's bill.

## Guest rounds and basket review

The basket response includes `purchaseFingerprint`, a SHA-256 digest of the canonical translated purchase tree and order type. It binds IDs, quantity, prices, ingredients, nested selections and exact preparation text, while ignoring display translations and line ordering.

`POST /api/table-guest-visits/{serviceSessionId}/rounds` is intentionally reachable without a login and requires `X-Table-Participant`, the existing basket `X-Session-Id`, and body `operationId`, `expectedAccountRevision`, `expectedBasketFingerprint`. Keep the reviewed digest and original operation until the outcome is confirmed. The server checks a committed operation before reading a possibly emptied basket; a new request with a changed basket or stale account is refused. The accepted batch attaches to the same visit and increments its account revision. Old rounds are not dispatched again.

Missing, expired, revoked or wrong-visit capabilities produce a generic unavailable response. Join is limited per IP; account reads and rounds have separate participant-digest rate limits with an IP fallback for malformed credentials and a separate coarse IP budget that also bounds random canonical tokens. Every guest response uses `no-store`. Error reporting strips both participant and basket capability headers; request bodies are excluded. Credential expiry/revocation does not imply record erasure: the cross-repo privacy/retention acceptance remains required before opt-in.

## Sources of truth

- DTOs: `Features/OrderAmendments/Dtos/`, `Features/TableGuestVisits/Dtos/`, `Features/Basket/Dtos/BasketDto.cs`.
- Authorization, replay and stale-input tests: amendment, guest round, account-read, rate-limit and fingerprint integration tests.
- Additive schema: `20261002192333_AddOrderAmendmentsAndGuestVisits` and `20261002194138_AddOrderAmendmentReplaySnapshots`.
- Cross-repo delivery and activation gates: workspace `docs/plans/TABLE-ACCOUNT-ORDER-AMENDMENTS-PLAN.md`.
