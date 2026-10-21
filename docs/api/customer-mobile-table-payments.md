# Native table-payment preparation

`POST /api/customer-mobile/table-payments/prepare` is a schema1 advisory read. It creates no
quote, hold, operation receipt, provider journal or tender and makes no provider request.
`table-payments` remains unknown in bootstrap; preparation alone does not enable the native
payment journey. This source contract requires subsequent durable submission and recovery.

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
using the same persisted DataProtection key ring. It currently authorizes no write endpoint.
The following implementation must persist a permanent native decision, atomically bind the
original shared quote/reservation, and preserve uncertainty through provider calls and process
death. No transaction may span provider I/O; a return link is navigation, never payment proof.

## Release boundary

No migration, tenant activation, frontend/printer contract change or production/provider/device
acceptance is included. Deploy the accepted source separately, verify disabled/enabled behavior
against the served version, and complete signed-link and physical native journey acceptance
before offering contributions.
