# Native owned reservation cancellation

These POST routes live under `/api/customer-mobile/reservations/cancellation`. They require a
current database Customer and the original canonical UUID `X-Session-Id`, use no-store and share
the native request budget. Guests, staff, machine credentials and invalid bearer tokens have no
fallback path. Unknown and foreign booking/operation authority use the same 404 response.

| Suffix | Body | Result |
|---|---|---|
| `/prepare` | `schemaVersion:1`, `operationId`, `tenantId`, `reservationId`, `timeZone` | Fresh customer booking projection, restaurant zone and minimal protected proof; no booking change or receipt |
| `/submit` | Original minimal proof | Atomically cancels exactly the reviewed booking revision and records Applied |
| `/recover` | Original minimal proof | Reads the canonical decision or Unseen without changing the booking or journal |
| `/cancel-attempt` | Original minimal proof | Reads the existing decision or permanently seals only the unseen cancellation attempt |

Every listed field is required; unknown fields are refused. The minimal proof has only
`schemaVersion`, `operationId`, `tenantId`, `payloadHash` and `ownerProof`. Its dedicated
DataProtection purpose binds the original Customer/session, tenant, operation, booking UUID,
database revision and zone. It cannot be substituted for a creation proof. It has no expiry or
contact fields. Retain the persisted DataProtection keys: key loss is not evidence of Unseen.

Preparation returns only reservation ID, calendar day, wall-clock start/end, party size, status
and customer special requests. It omits contacts, table assignments and staff/audit fields. The
client must show this fresh snapshot before explicit destructive confirmation. Preparation
reserves nothing and grants no permission to cancel a subsequent booking revision.

New preparation/submission requires enabled reservation and accounts capabilities, purchased
modules, active lifecycle, known currency, the selected tenant/zone, current Customer ownership
and a future Pending or Confirmed booking. Submission rechecks these after waiting and after
saving the status/receipt, before commit. A changed revision returns409 without an effect or
receipt. Do not repeatedly retry that stale review: retain/check/stop its original attempt,
then prepare a fresh review only after a canonical terminal decision is acknowledged.

Existing-booking writes acquire schedule authority, then the owned booking row, then Customer
row authority. This matches retained-data scrubbing before user deletion and prevents a
booking↔Customer lock inversion. The decision lookup comes before current booking checks:
replay returns its original result even if staff later reopens/deletes the booking or modules
are withdrawn. Original current-Customer/session and protected proof authority still apply.

Resolution is `{schemaVersion,operationId,outcome,reservationId}`. Applied means this original
cancellation committed, with the original booking ID; it does not describe later staff changes.
Cancelled means only this unseen attempt was stopped, with null booking ID. It neither cancels
the booking nor undoes Applied. Unseen is read-only uncertainty and cannot release the attempt.
Exact retry/recovery never reapplies a completed cancellation or sends mail again.

The status update, revision rotation and permanent generic operation receipt share one
transaction. Failed saves roll everything back and clear tracking. Mail starts after commit and
transaction disposal; a provider failure cannot change the canonical decision. No provider I/O
holds database authority. Keep minimal proof/session/owner in acknowledged SecureStore across
lost responses, restart, logout and tenant switches; no automatic write retry or new operation
ID can replace uncertainty. Account recovery requires the original Customer to sign in again.

Migration `20261020200457_AddReservationRevision` backfills nonempty UUIDs. Database triggers
rotate every parent insert/update, including raw SQL, and every affected old/new parent on
combined-table insert/move/delete. UUID revisions detect changes that application timestamps
or a wrapping transaction ID cannot reliably represent. EF reads generated revisions back.
Downgrade takes all booking/child/receipt table locks atomically with NOWAIT and refuses any
permanent operation history. Do not remove this barrier while recovery receipts exist.

The migration and contention/failure checks run only on disposable local test databases in this
work. Deployment/readback, installed native recovery, full locales/RTL and restaurant acceptance
remain separate gates. Owned booking edits use a separate contract.
