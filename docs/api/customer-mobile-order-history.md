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


## Owned receipt detail

GET /api/customer-mobile/orders/{orderId}?schemaVersion=1 requires the current database Customer
and matches the order owner in the same no-tracking query. Missing/deleted/anonymized/foreign orders
and withdrawn accounts return404. The native receipt controller applies no-store and bounded rate
policy; a UUID is navigation, never an ownership capability. No local operation proof is needed for
an order owned by the authenticated account.

The existing receipt reader shares bounded frozen composition and money/status projection. It reads
at most1001 order lines and10001 ingredient snapshots, refusing unsupported/oversized composition
rather than truncating it. No current catalogue graph, provider identity, contact or guest token is
returned. This adds no payment/order write, migration or notification.

Currency comes only from OrderBillingSnapshot. A legacy owned detail can return currency:null;
clients must hide every monetary label and line price and explain that the historical amount is
unavailable. Frozen names, quantities, options, instructions and separate current fulfillment/payment
status remain available. The existing proof-bound native operation receipt still requires recorded
currency and retains its original route/proof behavior. Cancellation/reorder and installed acceptance
remain separate work.
