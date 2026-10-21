# Native table round operations

The schema-1 routes under `/api/customer-mobile/table-rounds` add one unpaid round to an
existing shared table visit. They reuse the current guest order writer, routing and table-account
revision. They do not join a visit, close it, cancel an accepted order, collect a payment or redeem
personal points. Deployment and installed-device acceptance remain separate.

All four routes are POST, use the existing guest-round rate limiter and return `Cache-Control:
no-store`. The installed basket session must be a canonical non-empty UUID in `X-Session-Id`.
An anonymous basket owner or a currently active database Customer may act. Staff, machine tokens,
rejected JWTs and another Customer cannot act as that owner. A Customer JWT does not replace
`X-Table-Participant` for preparation or first acceptance.

## Review and first submission

`prepare` accepts only `schemaVersion`, `operationId`, `tenantId`, `serviceSessionId`,
`expectedAccountRevision`, `expectedBasketId` and `expectedPurchaseFingerprint`. All are required;
unknown fields are refused. The revision is a positive JavaScript-safe integer. The purchase
fingerprint is the original server-issued 64-character hex digest. The operation UUID is new.
Send the current table-participant token in `X-Table-Participant`.

Preparation takes basket-owner, native-operation, service-session and current-Customer authority
in that order. It checks current native ordering/table-visit capabilities, feature flags, DineIn
availability, live participant membership, exact account revision, original owned basket and matching
catalogue/visit currency. It returns `schemaVersion`, `proof`, the bounded native checkout `quote`
and a human `tableLabel`. Preparation creates no order, operation receipt or tender and leaves the
basket unchanged. Prices are shared guest-round prices: no Customer discount, points, tip or payment
method is introduced by owning the basket.

The service-session → User → basket order is consistent with permanent erasure's nested retained-data
scrubber, which locks related visits and then the User before deleting baskets. Real erasure/submit
tests observe the waiting PostgreSQL command in both directions: erasure first refuses the new round;
round first commits one shared order and both retained receipts before erasure completes.

Before sending, the native client must acknowledge durable storage of the exact original intent
and returned proof under its original environment, tenant, basket session and Customer/guest owner.
The client must also retain the visit participant credential separately. `submit` accepts that
same intent plus the returned `payloadHash` and `ownerProof`; it needs the original participant token
for a first acceptance. Do not reconstruct a prepared intent from a later basket or account read.

The protected, non-expiring proof binds its purpose, tenant, operation, original owner/session,
service session, physical table, participant and token hash, expected account revision, basket
fingerprint, currency and server quote. It also contains a separate random server-only operation
UUID for the legacy round receipt. The public native operation UUID cannot collide with an existing
web-round operation. Raw participant and basket-session credentials are not returned in the proof
projection or saved in either operation journal.

First acceptance rechecks the original review under the locks and writes one shared DineIn order,
its routing, the existing guest-round receipt, the account advance, basket clearing and the permanent
native receipt in the same transaction. Fresh owner, feature, participant, visit, currency and account
advance checks run after the actual final receipt save. Both durable receipts must agree before
commit. An exception rolls back the complete graph and clears tracking so an unrelated save cannot
resurrect it. Existing post-creation notifications run only after commit; notification failure never
turns an accepted round into an unseen attempt or triggers another round on replay.

## Recovery and stopping

`recover` and `cancel-attempt` accept only the returned `proof`: `schemaVersion`, `operationId`,
`tenantId`, `payloadHash` and `ownerProof`. All are required. They require the original basket session
and Customer/guest identity, but need neither current basket input nor the participant header.
They remain available to that original owner after the participant expires, the visit closes or
native capabilities are withdrawn. Customer database ownership is still checked; a rejected or
different Customer cannot fall back to guest recovery.

Every successful decision contains `schemaVersion`, `operationId`, `serviceSessionId`, `outcome`
and nullable `orderId`. Outcomes use the existing native order-operation enum:

| Outcome | Meaning |
| --- | --- |
| `Unseen` | No committed decision was observed. A delayed first request may still arrive. |
| `Applied` | The original round committed. `orderId` is the same on every replay. |
| `Cancelled` | The original unseen attempt was permanently sealed. No round was created. |

Stopping an applied attempt returns `Applied`; it does not cancel an order. Simultaneous submit
and stop serialize on the original operation, and both callers observe the first committed decision.
Keep original recovery custody after transport failure or `Unseen`; do not generate another
operation until the original decision is resolved or sealed. Changed input is refused even on replay.
An incomplete or mismatched pair of native/legacy receipts refuses both replacement and stopping.

The native journal uses the existing `20261019111141_AddCustomerOrderOperations` table with a
purpose-separated owner hash. No new schema or key-ring migration is introduced. Recovery depends
on retaining the original DataProtection key ring and both permanent receipts. Query/payload bounds
for the broader legacy table-account projection and operational deployment remain separate work.
