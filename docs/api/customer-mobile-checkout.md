# Customer mobile checkout quote

`POST /api/customer-mobile/checkout/quote` is an additive, native-ordering-gated review endpoint.
It requires the canonical lowercase, nonempty UUID `X-Session-Id` used by the customer's basket.
A supplied JWT must authenticate as Customer; rejected credentials do not downgrade to a guest.
Staff and machine principals are refused. A guest may review only its unowned session, and a
customer may review only its own basket. Responses use `ApiResponse<CustomerCheckoutQuoteDto>`
and `Cache-Control: no-store`. The route shares the native/legacy basket-add IP ingress budget.

All six request fields are required; null/default substitution and unknown fields are refused:

| Field | Contract |
| --- | --- |
| `schemaVersion` | `1` |
| `expectedBasketId` | Nonempty UUID from the last reviewed server basket |
| `expectedPurchaseFingerprint` | That basket's purchase digest |
| `type` | `Takeaway` or `Delivery`, matching the persisted basket |
| `pointsToRedeem` | Nonnegative integer; positive values require authenticated customer and native loyalty availability |
| `tip` | Nonnegative decimal with at most two meaningful fractional digits, within the existing order money limit |

No customer price, total, tender status, gateway reference, user identity or table number is accepted.
Dine-in will use the separate QR participant/round contract. A dedicated native endpoint must never
fall back to the legacy order API when unavailable.

The quote takes the same owner lock as basket writers and rechecks basket identity, purchase digest,
native ordering, enabled order type and opening hours. It prices the complete selected graph from the
current catalogue using the shared order pricing rules. It restores ingredient and product selections,
checks current product/menu availability, exact bundle rows, selected variations, side associations and
required sides. Unrepresentable selections are refused; no selected line is silently removed. Shape
budgets are 1,000 rows and eight nested levels; an unsupported review is refused without truncation.

The response contains basket/purchase identity, `quoteFingerprint`, currency, type, money breakdown,
root lines with nested selections and frozen ingredient presentation, and available payment methods.
Only roots carry `basketItemId`; transient draft order/item UUIDs are omitted. `priceChanged` compares
current root totals, delivery fee and discounts with the persisted basket. Tips and requested points
are explicit review choices. Current tax policy, customer discount rounding, points credit and tip
ordering remain the same as order pricing; this endpoint does not activate a new tax policy.

Available methods are Cash, plus CreditCard for Takeaway (card at the restaurant). Online payment
requires independently enabled native/provider availability; the current native capability policy
keeps it unavailable. A payment method in a quote is not a successful tender or payment authorization.

`quoteFingerprint` is a deterministic digest of reviewed purchase identity, current currency/type,
requested points, selected individual discount rule, normalized money, complete priced composition
and payment choices. Enumeration order and decimal trailing zeroes do not change it. It is a review
precondition, not a signature or a payment credential; an accepting endpoint must reprice server-side.

Repeated reviews do not create orders, order numbers, payments, loyalty transactions, basket receipts,
or notifications; they do not consume limited discounts or change the stored basket. Existing public
configuration readers may seed missing venue metadata. A quote is not an order acceptance receipt,
and cannot make an order/payment retry safe. Durable native order and payment operations are separate
contracts. No migration accompanies quoting. Source/CI acceptance does not prove deployment, flag
activation, provider settlement or device acceptance.

The HTTP and pure contract oracles are the `CustomerCheckout*Tests` integration suites. They include
independent totals and a canonical SHA-256 golden, repeated-read financial readback, catalogue changes,
ownership/rejected credentials, strict request validation, ingress limits and shape refusal.
