# Native hosted checkout

Status: provider adapter and durable journal in development. No native payment endpoint or activation is delivered by
this slice. The native app continues to refuse online payment until durable recovery is connected.

`CheckoutSessionRequest.NativeRouting` is optional for existing web callers. When absent, the
provider request retains its configured web success/cancel URLs, unset metadata and conditional
commission data. No customer email is prefilled. Dynamic tenant-supported payment methods remain
provider-selected.

For native recovery, the server must first durably freeze the entire request: order and operation,
amount/currency, nullable commission, provider account and test/live mode, original expiration,
idempotency key and canonical central return URL. The URL has the shape
`https://<reviewed-link-origin>/r/<tenant-slug>/payment-return/<order-id>?operation=<operation-id>`.
Both browser outcomes use this same navigation destination. It contains no authentication proof,
session credential, customer data or settlement claim. Configuration, journal and authorization
must supply the approved origin; URL structure validation alone does not establish origin trust.

The adapter refuses provider account/mode drift before creation. Session and PaymentIntent metadata
carry the native operation and schema; canonical session evidence must match the frozen account,
mode, order, operation, currency and amount. `open`, delayed `complete/unpaid`, explicit expiry and
future statuses remain distinct. This identity validation neither settles a tender nor proves that
browser navigation completed payment.

Stripe can prune API v1 idempotency keys after 24 hours. Recovery must replay the original payload
within a bounded earlier deadline, then hold an unknown outcome for reconciliation. It must never
change the expiry, recalculate money, rotate the key or mint a replacement to overcome an unknown
result. See [Stripe idempotency](https://docs.stripe.com/api/idempotent_requests) and
[Checkout session creation](https://docs.stripe.com/api/checkout/sessions/create).

The journal store commits one original request per accepted native order, under the original private
operation proof and current customer/guest ownership. It matches erasure's Order-before-User lock
order, requires the original billing snapshot/processing online intent and rejects changed accepted
money, table-account orders and existing web sessions. Existing journals recover their original
request even when current configuration changes; this is not permission to create at the provider
after activation is withdrawn. The new settings default disabled. The public capability remains
unknown and the native app still refuses online payment.

Migration `20261019193218_AddCustomerOrderCheckoutJournal` stores opaque references and frozen
money/routing evidence without customer contact or credentials. Unique order/operation/key/session
constraints prevent a replacement request. Nonempty recovery history refuses downgrade under an
exclusive table lock. Account/order erasure cannot cascade this financial recovery record away.
The ordinary web creation path refuses native operation orders before provider I/O.

Required follow-through: short isolated lease/evidence write phases and canonical reconciliation;
proof-bound customer/guest endpoints; client SecureStore persistence and browser/foreground recovery;
central verified links; process-death/concurrency/provider acceptance. These remain open, as do
signing, deployment and installed iOS/Android payment journeys. Adapter tests use mocked transport.
