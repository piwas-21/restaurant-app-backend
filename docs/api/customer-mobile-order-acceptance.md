# Native customer order acceptance

All routes are under `/api/customer-mobile/checkout`, return an `ApiResponse` envelope with
`Cache-Control: no-store`, and share the configured native/legacy basket-add IP budget. They require
the original canonical lowercase UUID `X-Session-Id`. Present authentication must be a valid customer
JWT; staff/machine/failed authentication cannot downgrade to guest. No legacy-order fallback exists.

| POST route | Request | Result |
| --- | --- | --- |
| `prepare` | Typed intent | Schema 1, server-computed payload hash and current quote; no acceptance |
| `submit` | Schema 1, operation UUID, payload hash, original intent | Applied proof with one order UUID |
| `recover` | Schema 1, original operation UUID and payload hash | Unseen, Applied or Cancelled proof |
| `cancel` | Same minimal recovery request | Cancelled tombstone if unseen; original Applied proof otherwise |

An intent requires `review` (the six [quote](customer-mobile-checkout.md) fields),
`expectedQuoteFingerprint`, `contact` (name/email/phone), and `paymentMethod`. Optional `notes` retain
the existing order limit. Delivery requires `address` with street/city/postalCode; country defaults to
empty because the tenant registry makes it optional. Country/additionalInfo requiredness is rechecked
against current tenant settings. Address data is refused for Takeaway. Only known fields bind;
client prices, paid state, gateway references, user/table IDs and saved-address selectors are excluded.
Cash, card at restaurant and online intents must be offered by the current quote. Online remains
unavailable under the current native capability policy; this contract does not activate it.

Preparation checks the owner, current basket/review, catalogue, money, methods and form requirements
under the shared owner lock. It returns a deterministic server digest of the exact typed intent,
including contact/address and quote precondition. The digest is not a signature or authorization.
The client need not implement a second cross-language canonical JSON encoder. Public configuration
readers may initialize missing venue metadata; preparation never allocates an order number, consumes
a discount, debits points, creates a tender/receipt, clears the basket or sends notifications.

Submission recomputes the digest and then uses the durable order operation store. Matching receipt
replay happens before new availability, pricing, number allocation or notification work. First
acceptance rechecks the reviewed basket/quote and form requirements, builds server-priced items,
uses the shared order factory/writer with strict points redemption, and compares accepted money and
composition with the reviewed quote. A mismatch rolls back order, snapshot, discount usage, points,
tenders and receipt. Basket clear is part of that same transaction. No tender starts captured.
Legacy order creation retains its existing default points policy.

The response is minimal schema/operation/outcome/order-ID proof; it contains neither contact data nor
operator or guest action tokens. Private order detail and hosted payment recovery are separate work.
Notifications run only for first acceptance after commit. Delivery failure cannot change an accepted
outcome or rerun notification on replay; durable notification delivery remains a separate requirement.

Recovery needs only the original owner/session, operation UUID and digest, so contact data can be
purged separately on logout. Unseen is not proof that an earlier request cannot arrive. Persist the
customer's cancellation choice before calling cancel; its tombstone seals late execution under the
same owner/operation locks. Cancel never reverses an accepted order, refunds money or restores a basket.

This contract requires the additive operation-store migration; no further migration is added here.
Migration application, deployment/readback, connected native checkout, payment/provider recovery and
installed-device acceptance remain distinct release gates.
