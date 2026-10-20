# Native customer delay rejection

These routes decline a restaurant preparation delay for a local standalone order still awaiting
customer approval in the `direct` confirmation flow. They do not authorize general accepted-order
cancellation. The staff cancellation route and public email actions retain their existing contracts.
Source integration does not enable native ordering on a tenant or apply its migration.

| Authority | Prepare | Submit | Recover |
| --- | --- | --- | --- |
| Current authenticated Customer | `GET /api/customer-mobile/orders/{orderId}/cancellation?schemaVersion=1` | `POST /api/customer-mobile/orders/cancellation/submit` | `POST /api/customer-mobile/orders/cancellation/recover` |
| Original native checkout proof | `POST /api/customer-mobile/orders/cancellation/original/prepare` | `POST /api/customer-mobile/orders/cancellation/original/submit` | `POST /api/customer-mobile/orders/cancellation/original/recover` |

All responses use the API envelope with `Cache-Control: no-store` and the existing native ordering
rate policy. Original routes require the canonical installed `X-Session-Id`; prepare takes the strict
original checkout recovery proof. Submit/recover take `{ proof, intent }`. Signed customer routes
accept only the strict intent. Staff, machine identities, another customer, and rejected/expired JWTs
cannot borrow guest authority. The live database owner is checked under the order lock on every phase.
An anonymous deep link cannot mutate an order. Proofs never belong in a URL.

Prepare returns schema version 1, order ID, current expected version, `allowed`, refusal reason and,
only when allowed, the canonical payload hash. It changes nothing. The strict intent contains
`schemaVersion`, one fresh non-empty `operationId`, `orderId`, `expectedVersion`, and that `payloadHash`.
The digest binds schema, order, expected version and the fixed `DeclinePreparationDelay` action.
No amount, refund, payment instruction, customer identity or contact field is accepted.

Submit serializes its operation and shares the existing order/account lock. It rechecks the current
version, ordering capability, enabled order channel, confirmation flow, local standalone membership,
prior staff work and financial clearance. Table accounts, assigned dine-in, external orders, any
provider checkout/journal, account allocation, captured/enriched/extra tender, loyalty movement or
redemption, amendment, credit or bill charge requires restaurant review. One ordinary pending cash
or takeaway card-at-restaurant intent may remain unchanged; no collected money is reversed. The
narrow action changes status/audit/version only. It never calls a payment provider, refunds money,
changes an accepted billing snapshot or moves loyalty. Operational notification runs after commit;
notification failure cannot change the canonical result.

Both `Cancelled` and `Refused` are terminal retained decisions. The resolution contains schema,
operation/order IDs, outcome, observed version and refusal reason. Exact retries return that same
result without a second status event. Reusing an operation for another payload or owner is refused.
A later availability or version change cannot turn a previous refusal into a cancellation.

Recovery without a decision returns `Unseen` with no observed version/reason and writes nothing.
**Unseen is not proof an earlier request cannot arrive later.** Native callers must securely retain
one original intent until its canonical result is resolved. An ambiguous outcome must not create a
replacement operation, infer cancellation from a status refresh, or unlock a competing write. Exact
same-operation retry is idempotent; signing out or switching restaurant must retain the pending hold
without exposing the original proof to the next customer.

Migration `20261020031044_AddCustomerOrderCancellationOperations` stores only operation/order UUIDs,
owner/payload hashes, expected/observed versions, terminal outcome/reason and UTC completion time.
There is no contact data or order FK/cascade. Database checks reject contradictory shapes and row
update/delete triggers preserve decisions. Downgrade holds an exclusive table lock and refuses any
retained decision; only an empty new table can be removed.

Native confirmation UI, secure pending-intent recovery and installed-device acceptance remain
required before this action can be claimed as available in the app.
