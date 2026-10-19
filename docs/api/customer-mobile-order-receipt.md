# Native customer order receipt

`POST /api/customer-mobile/orders/receipt` is a read-only operation with the original canonical
`X-Session-Id` and strict schema-1 `operationId`/`payloadHash` body. The capability is kept out of URLs.
Present authentication must be a valid customer session; staff/machine/rejected authentication cannot
downgrade to guest. The durable operation must resolve Applied for that exact owner/session/digest.
Unseen, Cancelled, missing/deleted/anonymized orders produce no receipt. No caller-supplied order ID
can select another order. Ordering/channel withdrawal does not invalidate an original accepted proof.

The no-store envelope contains schema version, order identity/number/type/date, separate fulfillment
and payment status, frozen accepted currency, restaurant timezone, promised ready/delivery times,
confirmation flow/deadline, server money, current payable/credit/charge/paid/remaining amounts and
frozen nested item/ingredient composition. It includes no contact/address, JWT, operation digest,
operator action token, guest status token, gateway reference or customer/staff identity.

The reader never reprices against today's catalogue, changes currency from current restaurant
settings, settles a payment, allocates an order, sends notifications or mutates the basket. Item and
ingredient reads have explicit row budgets (1,000 and 10,000); unsupported composition is refused
rather than truncated. The quote and receipt share bounded depth/cycle/composition projection.

The POST shares the configured basket/native-checkout IP budget and requires no new migration.
Accepted source, deployment/migration application, connected native receipt UI and installed-device
acceptance remain separate evidence. Customer account history and hosted payment recovery are
separate contracts; this endpoint is limited to the original accepted native operation.
