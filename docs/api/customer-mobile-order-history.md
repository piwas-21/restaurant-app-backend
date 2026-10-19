# Native customer order history

`GET /api/customer-mobile/orders?schemaVersion=1` is a private, read-only customer summary.
The current database account must remain an active Customer. JWT role alone is insufficient.
The endpoint returns no contact/address details, operation proofs, provider identifiers, notes,
staff fields or order-line/catalogue graph. No-store and the existing customer request rate limit apply.

The response contains schemaVersion1, businessTimeZoneId, at most20 items and nullable nextBefore.
Each item contains orderId, orderNumber, type, orderDate, status, paymentStatus, currency and payableTotal.
Orders sort newest UTC orderDate first, then UUID ascending for deterministic ties. Supply both
beforeOrderDate and beforeOrderId from nextBefore for the next page; neither value selects an owner.
Invalid/missing versions and incomplete/empty-ID cursors are rejected. Deleted, anonymized, guest
and foreign orders are excluded by current ownership. An existing empty account returns an empty page;
a missing/deleted/non-customer account returns404.

A single no-tracking database projection filters the current account and owner before ranking and
bounds the inner order window to21 rows. There is no unbounded total count or per-order HTTP request.
The extra row determines continuation; only20 summaries are returned.

Currency comes only from an immutable OrderBillingSnapshot. Legacy orders without a valid recorded
currency remain in history with both currency and payableTotal null; today's restaurant currency is
never substituted. With recorded currency, payableTotal is the current accepted order total plus
bill charges minus billing credits. It is separate from paymentStatus and does not claim settlement,
amount paid or a provider outcome. Detailed receipts, reorder and permitted accepted-order cancellation
remain separate customer journeys.

This source adds no migration, retention change, order/payment write or native activation. Source
review/CI, deployed contract readback and installed customer acceptance are separate requirements.
