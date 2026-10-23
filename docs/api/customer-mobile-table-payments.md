# Native table-payment preparation and original decisions

`POST /api/customer-mobile/table-payments/prepare` is a schema1 advisory read. It creates no
quote, hold, operation receipt, provider journal or tender and makes no provider request.
`table-payments` remains unknown in bootstrap; preparation alone does not enable the native
payment journey. Native contribution submission and original decision recovery are additive below;
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
even if the caller knows the private shared operation ID. Native hosted checkout is not offered by
this slice. All actions share the existing bounded ingress policy and private/no-store responses.

## Release boundary

The additive `20261023010734_AddCustomerTablePaymentOperations` migration adds a permanent
UUID-keyed decision with bounded owner/payload digests, a nullable unique attempt reference, a stop
marker and UTC completion time. No account/attempt foreign key may erase the decision. Downgrade
refuses committed history and in-flight writers. Historical migrations are unchanged. The migration
has only local disposable PostgreSQL evidence; staging/production application is separate.

No tenant activation, frontend/printer contract change or production/provider/device acceptance is
included. Deploy the accepted source separately, verify disabled/enabled behavior
against the served version, and complete signed-link and physical native journey acceptance
before offering contributions.
