# Native order operation persistence

`ICustomerOrderOperationStore` supplies an internal atomic boundary for future native order
submission. This slice adds no public route and does not enable native ordering or payments.

The caller supplies a canonical installed basket session, a non-empty operation UUID and the
server-computed hash of its strict original request. Customer/guest authentication is checked before
entering a transaction. Staff, machine tokens and rejected authorization headers are refused.
Owner hashes bind both the customer identity and installed session; another owner receives no order ID.

The store owns a transaction, takes the existing basket-owner advisory lock and then a separately
namespaced order-operation lock. Effects use the same scoped context, save an order for the current
owner and return its ID. The operation receipt commits with that order and all transactional effects.
Repeated execution returns the original ID without invoking the effect again. Availability and
catalogue checks belong inside the first-acceptance callback, after receipt replay is resolved.

Recovery is read-only. `Unseen` describes only this read: it does not prove an earlier request cannot
arrive later. Cancellation records a durable tombstone for an unseen operation, sealing late execution.
Cancellation of an applied operation returns its accepted order; it never cancels or refunds the order.
All three paths require the same owner and original payload hash.

Entry requires a clean change tracker and no caller transaction. On failure the coordinator rolls
back, then the store detaches all order, financial, basket and receipt state. A later save in the same
context cannot resurrect an aborted effect or cancellation outside these locks.

`20261019111141_AddCustomerOrderOperations` creates `customer_order_operations`: UUID operation/order
IDs, 64-character owner/payload hashes, cancellation decision and UTC completion timestamp. No contact
data, token or request body is stored. There is no order foreign key/cascade: removal of an old order
must not permit a duplicate submission. Database constraints distinguish applied from cancelled rows.
The migration refuses downgrade with any receipt present while holding an exclusive table lock.
Newer empty tables can be removed before an older migration's retention guard refuses downgrade;
historical tests assert that guard's boundary and restore the current schema before lane reset.

Migration application, strict public submission/recovery contracts, current-price and quote comparison,
secure device receipt/journal integration, post-commit delivery and end-to-end payment recovery remain
required. The callback must perform no external notifications or payment-provider writes inside this
database transaction. Source tests are not deployment, provider or installed-device acceptance.
