# Native hosted checkout

Status: provider adapter, durable journal, canonical settlement, background reconciliation and private-proof
payment recovery in development. Native client recovery, verified returns and activation remain open.
The native app continues to refuse online payment until its durable recovery flow is connected.

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
future statuses remain distinct. Browser navigation does not establish settlement.

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

Lease acquisition and canonical-evidence recording use separate short transactions. Both recover the
original operation proof and recheck the current order owner under the Order-before-User locks.
A minute-long durable lease prevents concurrent publication; expiry permits only the same original
request within its original replay deadline. Expired/replaced workers cannot record results. Provider
create and canonical reread occur outside database transactions. Missing sessions, mismatched evidence,
transport failure and late responses retain the original request and processing intent. Known-session
recovery never falls back to another create, including after settings withdrawal or the retry deadline.

Canonical recording retains the provider reference even if current creation eligibility changes.
The recovery result withholds the payable URL after closure, part-payment, capability/settings withdrawal
or original session expiry. Hosted pages also require an explicitly approved HTTPS origin; control
characters, credentials, backslashes and nonstandard ports are refused.

## Private payment recovery

`POST /api/customer-mobile/checkout/payment/recover` requires the original canonical `X-Session-Id`
and strict schema-1 `operationId`/`payloadHash` body. The order is resolved from that proof, never
from a supplied order ID or amount. Invalid/expired authentication cannot become guest access;
staff cannot borrow a guest proof. Current database ownership is checked before provider work,
at evidence commit and again after effects before returning private data. Ordering withdrawal may
block continuation without erasing an accepted payment's financial evidence.

The no-store schema-1 response contains only `operationId`, `orderId`, `outcome`, nullable
`checkoutUrl` and the original `expiresAt`. Outcomes are `ready`, `pending`, `paid`, `expired` or
`failed`. Only `ready` contains an approved hosted URL. This describes the original payment attempt;
the receipt independently describes current fulfillment and balance. Unknown outcomes offer no
replacement charge. The route shares the configured native basket/checkout IP rate budget.

## Settlement and unattended recovery

A terminal session and tender commit in the same transaction as the journal evidence. A paid session
must be canonically complete/paid, with its exact PaymentIntent succeeded and the full original
amount received. The intent must independently match its session ID binding, provider account/mode,
native operation/schema, currency, amount and nullable application fee. Partial, delayed, unsupported
or contradictory evidence stays pending. Explicit expired/unpaid or completed/unpaid with a failed,
zero-received canonical intent can fail only the original processing online tender. Payment never
auto-confirms or cancels fulfillment. Captures after cancellation or other tenders remain recorded;
overpayment remains visible. Replays verify the retained tender rather than writing another one.

Ordinary web settlement rejects native operations both before terminal reads and at the writer.
The existing ordinary expiry sweep runs first, then a bounded native reconciliation pass. Native work
claims a short lease and commits its rotation before validating retained/provider inputs, so poison
rows back off. Provider reads occur outside database transactions. Server financial recovery does
not require a surviving customer account or an app return, and never creates a replacement session.
Failure diagnostics retain only the opaque order ID and exception classification, excluding raw
provider exception messages, URLs and credentials.

Migration `20261020002402_AddCustomerCheckoutEffectRecovery` adds nullable UTC `effects_completed_at`
to the retained journal. Completed payments without this marker remain background candidates even
when no app returns. Loyalty uses the strict existing award authority; mail queues run independently.
Queueing or an in-flight email claim cannot complete effects: both required outbound ledgers must
record sent evidence, and loyalty must no longer be deferred. Existing sender claims and loyalty
journals deduplicate retries. Completion does not clear another worker's lease. Cancellation/deletion
can make effects inapplicable without removing financial evidence. The existing sender's residual
provider-send/mark-sent uncertainty remains; no exact-once external-delivery claim is made.

Required follow-through: client SecureStore persistence and browser/foreground recovery; central
verified links; provider acceptance and deployed readback. Signing and installed iOS/Android payment
journeys remain open. Tests use isolated database fixtures and mocked provider/mail transport.
