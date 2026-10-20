# Native reservation creation

All routes below are POSTs beneath `/api/customer-mobile/reservations/create`, use no-store
and share the native request budget. `X-Session-Id` is a canonical nonempty UUID held as a
private session capability. Rejected/expired bearer authentication never falls back to guest
access. Staff and API credentials cannot use these routes. Current database Customer ownership
is checked before preparation and under row authority for operation resolution/acceptance.

| Suffix | Body | Behavior |
|---|---|---|
| `/prepare` | Full draft with null/omitted payloadHash and ownerProof | Returns minimal original proof; creates no booking or receipt and reserves no capacity |
| Base route | Full draft with the prepared payloadHash and ownerProof | Recomputes original input/owner binding and atomically accepts one Pending booking |
| `/recover` | Original minimal proof | Reads Applied/Cancelled, or Unseen without creating a receipt |
| `/cancel-attempt` | Original minimal proof | Returns an existing decision or durably seals the unseen attempt |

The full draft contains `schemaVersion:1`, `operationId`, `tenantId`, `timeZone`, `date`,
`startTime`, `endTime`, `numberOfGuests`, `customerName`, `customerEmail`, `customerPhone`,
`specialRequests`, `payloadHash` and `ownerProof`. `date` is a restaurant calendar day; times are restaurant
wall times. No table, status, caller identity, staff note or action-token fields are accepted.
Party size is 1–20; contact/request lengths match existing reservation columns. The existing
customer form settings control required phone and special requests; clients reuse
`/api/FormFieldConfiguration`.

Preparation returns `{schemaVersion,operationId,tenantId,payloadHash,ownerProof}`. The hash binds the exact
normalized full input and original owner/session. It is not an availability promise or a booking.
Create verifies that hash even when replaying a known receipt: changed input cannot masquerade
as an exact retry. Recovery/cancel accept only this minimal descriptor and original private
session/principal; contacts are unnecessary and unknown extra fields are refused.

New acceptance requires fresh enabled native reservation capability, purchased module, active
lifecycle, known currency and the selected tenant/timezone. Authenticated acceptance also
requires the current native accounts capability. Under the shared tenant-database schedule
lock, the server recomputes serving-window/combined occupancy, chooses a smallest fitting single
table or enough available positive-capacity tables, and checks their current capacity using a
fresh selected-table read. Immediately before acceptance, today's start must still be future on
the tenant clock. New bookings remain Pending until restaurant approval.

The shared writer assigns the new server UUID before staging its row. The booking and operation
receipt save/commit together; mail runs afterward and remains best effort. Schedule authority
precedes the customer-row guard, and original tenant/principal scope is rechecked after waiting.
Legacy creation uses the same draft rules and retains its API contract. Never dispatch a legacy
transaction-owning handler inside an outer native transaction.

Resolution is only `{schemaVersion,operationId,outcome,reservationId}`. Applied is stable
acceptance, not restaurant confirmation or private-detail read authority. Cancelled durably seals
that unseen create attempt and has null reservationId; cancel-attempt never cancels an applied
booking. Unseen is an observation, not cancellation or permission to replace the original
operation. Exact decisions survive capability/module withdrawal and booking deletion without
sending mail again. Current account/original session binding still apply. A foreign owner gets
the same 404 as an unavailable operation; changing a bound payload gets 409.

Clients must durably acknowledge the exact scoped original proof and private intent before
submitting creation. Keep both in SecureStore; never invent a replacement operation ID or change
its body after acknowledgement. Preserve unresolved minimal proof/session/principal across
network loss, process termination, logout and tenant/account switching. Logout may purge contact
fields without losing recovery; account recovery still requires its original customer to sign in.
Minimal recovery never reconstructs or resubmits a booking. If Unseen, keep uncertainty pending
until explicit cancel-attempt returns a canonical decision. Do not silently retry offline writes.
An unsuccessful response or browser return cannot prove that a booking was cancelled.

Migration `20261020173728_AddCustomerReservationOperations` adds receipt identity, owner/payload
hashes, nullable booking reference, outcome and UTC completion time. It stores no contacts,
request body, credentials or direct customer identity, without a booking/user foreign key.
Constraints reject inconsistent outcomes. Downgrade refuses a nonempty receipt table under an
exclusive lock, including a concurrent insert. No expiry/cleanup removes receipts. This work
applies it only to a separate disposable local template; staging/production migration and release
acceptance remain separate.

The allocation/response is small, but the legacy engine still scans active tables and that day's
bookings. Response limits do not bound scan/sort work. Large schedules, queueing, deployed
readback and installed performance remain rollout gates. Owned native edit/cancellation, the
creation UI/journal, locale review and installed journeys are separate work.

Preparation includes a non-expiring purpose/version-bound DataProtection proof covering tenant, operation, original owner/session hash and canonical payload hash. Create, recover and cancel-attempt verify it against current authority, including again after a schedule wait. It contains no contacts or direct customer identity. Existing persisted DataProtection keys must be retained for durable recovery; key loss cannot be treated as an unseen or cancelled attempt.
