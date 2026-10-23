# Native table contributions and hosted checkout

`POST /api/customer-mobile/table-payments/prepare` is a schema1 advisory read. It creates no
quote, hold, operation receipt, provider journal or tender and makes no provider request.
`table-payments` stays unavailable without separate native readiness approval; preparation alone
does not enable the payment journey. Native contribution submission and original decision recovery are additive below;
hosted checkout, native return routing and activation remain separate gates.

## Request and authority

Supply canonical `X-Session-Id`, separate live `X-Table-Participant`, and Customer bearer
authority when present. Invalid, staff or deleted-account credentials cannot become a guest.
Explicit JSON fields are `schemaVersion:1`, configured `tenantId`, new public `operationId`,
original `serviceSessionId`, positive exact `expectedAccountRevision` and `mode`.
Unknown fields are refused. Input is limited to128KiB and shares existing coarse-IP and
participant account budgets with the current-account read.

Modes reuse the shared server quote rules:

| Mode | Additional input |
| --- | --- |
| `Full` | None; current available debt |
| `Amount` | Positive integer `amountMinor` |
| `Items` | Distinct `{orderId,orderItemId,ordinal}` entries in `selectedUnits` |
| `Equal` | Existing `equalSharePlanId` and positive `equalShareOrdinal` |
| `CustomAmount` | Existing `customSharePlanId` and positive `customShareOrdinal` |

No tip, payment method, contact, alternate currency or return URL is accepted. Guest payment
policy requires the actual online-payment module, table/guest-payment flags, configured
provider and settlement-currency contribution limits. Native visits/payments and any Customer
account require explicit rollout. Reviewed hosted-link settings must be valid and enabled;
this work does not activate those settings or establish deployed association/signing evidence.

## Review and original proof

A repeatable-read snapshot checks the original open visit/revision/currency and its reconciled
ledger, then applies the same scope selector used by existing web/staff quotes. Selected-unit,
scope-segment, historical capacity and online contribution limits still apply. Schema1 refuses
inexact JavaScript revisions/amounts and more than5,000 returned scope segments. These response
bounds do not bound the legacy debt-reader SQL scans.

The response contains the server amount/currency, exact reviewed allocation ranges, current
account revision and a protected proof envelope: schema, tenant, public operation, payload hash
and opaque `ownerProof`. The proof binds canonical session/Customer owner hash, actual visit,
table and participant identity/token hash, a separate random private legacy operation, request
and exact allocation hash. It contains no contact fields or raw participant token. The private
legacy identifier keeps the public native operation outside existing guest/staff namespaces.
Item ordering has one canonical input hash; changing any selection, amount, mode or slot
requires a new review.

Native rollout, current Customer, currency and the same live participant are rechecked after
the snapshot transaction ends. A proof produced during a withdrawn read is withheld. Successful
responses are private/no-store. A prepared amount is advisory and can become stale immediately;
no money is reserved and the customer must not be told that a share has been claimed.

The non-expiring proof is intended for original minimal recovery/stopping after visit expiry,
using the same persisted DataProtection key ring. Submission revalidates the live scope; the proof
never substitutes for current write authority. No transaction may span provider I/O; a return link
is navigation, never payment proof.

## Durable acceptance and original recovery

`POST /submit` accepts exactly `{intent, proof}` from the original review, with the same canonical
session/principal and live participant header. The proof envelope is strict and requires all its
schema/tenant/operation/hash/owner-proof fields. Null or missing objects are refused. New acceptance
rechecks current native/account/guest/provider policy, participant/token, visit/table and revision.
The shared quote selector must reproduce the exact prepared amount, currency and allocations.

One short transaction locks the visit, current customer when present and the native public operation.
It creates the shared online quote and reservation and inserts the permanent
`customer_table_payment_operations` decision atomically. Failure after actual reservation rolls back
all three. Concurrent identical submissions recover one decision; competing contributions cannot
claim the same available debt. The server-only legacy operation ID never becomes the public native
operation ID. No provider request, journal, tender or paid claim is created by submission.

`POST /recover` and `POST /stop` accept only the original proof plus canonical session/principal;
they need no current participant and remain available after visit expiry/closure or capability
withdrawal. Invalid JWTs still cannot downgrade to guest access. An unseen recovery returns `Unseen`
without creating anything. Stopping unseen work permanently records `Stopped`, so a late submission
cannot create a payment. Reusing the public ID with another preparation/proof is refused.

An already applied decision returns `Applied` with its **attempt ID**, visit and current payment
state. `Applied` means the original reservation was accepted; it does not mean payment succeeded.
Stopping applied work returns the retained decision and never releases its reservation, cancels a
provider operation or promises non-payment. Provider cancellation belongs to the subsequent canonical
checkout rail. Missing or inconsistent retained attempts/allocations require reconciliation and cannot
become `Unseen` or authorize replacement. Reads verify the protected original scope against the
frozen quote and persisted allocations; SQL child ordering does not change identity.

The ordinary web checkout journal refuses native reservations before provider request creation,
even if the caller knows the private shared operation ID. Ordinary web cancellation also refuses
native journals. All actions share the existing bounded ingress policy and private/no-store responses.

## Frozen hosted checkout

The three `POST /api/customer-mobile/table-payments/checkout/{start|recover|stop}` actions accept
only the original proof as the body, with the same canonical session/principal. `start` additionally
requires the original current participant when no journal exists. It rechecks current tenant policy,
feature/module/provider settings, currency, visit/table, reservation expiry and outstanding debt,
then commits the frozen journal and `Starting` state in a short visit→customer→attempt→journal
transaction. No provider network call runs in that transaction. First-party native request authority
cannot be replaced with a web receipt credential or another preparation.

The shared leased reconciler makes the provider call after commit. A lost response replays the same
merchant, environment, amount, currency, expiry, return base, idempotency key and native tenant/public
operation/attempt tuple. A known session is read canonically and never recreated. After the original
expiry or retry deadline, missing evidence stays pending. Native metadata is copied to both Checkout
Session and PaymentIntent. Session, intent and charge identity must all match; complete+paid Session,
succeeded full intent, exact unreversed captured charge and posted financial allocations are required
before `paid`. Contradictory capture evidence is retained for reconciliation without releasing debt.
Ordinary frozen request JSON omits the optional native scope, preserving existing hashes byte for byte.

Recovery and stopping continue with the original proof after participant expiry, closure or rollout
withdrawal. A reserved attempt with no journal and no escaped-provider markers can be stopped locally;
late start cannot revive it. A frozen journal receives a cancellation request and retains its reservation
until canonical provider evidence proves non-payment. Capture winning cancellation is posted once.
An unknown provider result cannot authorize release, a new operation or replacement checkout.

The minimal schema1 response carries `tenantId`, public `operationId`, `serviceSessionId`, explicit
`attemptId` (nullable for a stopped original decision), original `amountMinor`/`currency`, frozen
`expiresAt`, `outcome` and nullable `checkoutUrl`. Outcomes are `reserved`, `pending`, `ready`,
`paid` or `stopped`. A browser URL is returned only for canonical open unpaid evidence, an unexpired
journal, current participant/creation policy and an approved HTTPS hosted origin. Recovery after
withdrawal retains the original decision with no browser offer. Private owner authority is rechecked
after provider and posting work. There are no order, allocation, merchant or participant fields.

The frozen central return is `/r/{tenantSlug}/table-payment-return/{attemptId}?operation={publicOperationId}`.
Success and cancel use the same path. A separate attempt-reference navigation contract validates
configured host, directory status, schema and canonical IDs. The public page discloses no payment
state or private proof; it only asks the customer to reopen the original contribution. Association
paths explicitly include this route. A URL or browser return never proves payment or cancellation.
Actual OS link verification remains a signed/deployed acceptance gate.

## Native readiness and rollback

`CustomerHostedCheckout:TablePaymentsEnabled` is a separate approval switch and defaults to false.
Existing configured hosted checkout never opts a tenant into native table payments. Bootstrap can
report `table-payments: enabled` only when its existing capability/lifecycle/module/visit gates pass,
hosted checkout and exact routing are valid, and the shared guest policy reports matching settlement
currency and valid provider limits. This reads local configuration only; it neither probes nor creates
provider sessions. Unknown/unproved readiness withholds new native preparation, submission and start.

Withdrawal keeps the permanent operation and original financial scope. Original recovery and stopping
remain available; a known capture still becomes canonical paid, and current approval withdrawal
withholds a browser URL. An unseen proof can still be permanently stopped before later reactivation.
No environment or tenant approval switch is changed by the source implementation.

## Release boundary

The additive `20261023010734_AddCustomerTablePaymentOperations` migration adds a permanent
UUID-keyed decision with bounded owner/payload digests, a nullable unique attempt reference, a stop
marker and UTC completion time. No account/attempt foreign key may erase the decision. Downgrade
refuses committed history and in-flight writers. Historical migrations are unchanged. The migration
has only local disposable PostgreSQL evidence; staging/production application is separate.

The additive `20261023015615_AddNativeTableCheckoutScope` migration adds nullable paired native
tenant/operation identity to the existing checkout journal, a unique operation reference and a
restrictive foreign key to the permanent decision. Ordinary rows retain null scope. Downgrade uses
NOWAIT and refuses any retained native provider request; ordinary frozen journal data survives an
empty-scope rollback/upgrade. Background reconciliation cadence and retention are unchanged.

No tenant activation, frontend/printer contract change or production/provider/device acceptance is
included. Deploy the accepted source separately, verify disabled/enabled behavior
against the served version, and complete signed-link and physical native journey acceptance
before offering contributions.

## Native split-plan operations

`POST /api/customer-mobile/table-payments/plans/{prepare|submit|recover|stop}` uses separate
schema1 intent and purpose-bound original proof. Preparation is read-only: review the original
visit/revision, share count, ordered custom amounts (or an empty list for equal shares), and an
optional explicitly superseded plan. It returns the exact server-owned total, currency, frozen
allocation scope, cash rounding increment and individual share amounts. Each share must satisfy
current online contribution limits. The native bound is100 shares and the existing configured
segment bound; no quantity expansion is performed.

Submit needs the current original participant/session/customer, native approval and current
policy. It revalidates the frozen scope and distribution, calls the shared plan writer under a
short visit→customer→native-operation transaction, then atomically retains the original decision.
The server's internal guest-plan operation differs from the app's public operation. Existing
supersession ownership and protected/captured-contribution refusal remain authoritative.

Original-proof recovery discloses only schema, original operation, visit, outcome and nullable
plan UUID. Outcomes are `unseen`, `applied` or `stopped`. Unseen is not proof that a late submit
cannot arrive. Stop permanently seals unseen work; it never invalidates an applied shared plan,
releases a contribution or performs provider I/O. Recovery/stopping keep the original authority
after participant expiry or rollout withdrawal. Retained missing/changed plans fail closed.

The separate permanent `customer_table_plan_operations` table stores only public operation UUID,
owner/payload digests, nullable plan UUID, stopped flag and completion time. It has no erasure-
cascading account/participant/plan FK. Its additive migration refuses downgrade while any decision
or in-flight decision writer exists. Preserve original DataProtection keys for proof recovery.
No signing, production activation, provider charge or installed acceptance is implied.
