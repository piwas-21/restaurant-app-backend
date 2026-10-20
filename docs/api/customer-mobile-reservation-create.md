# Native reservation creation

`POST /api/customer-mobile/reservations/create` accepts schema 1 guest or Customer bookings.
`POST /api/customer-mobile/reservations/create/cancel-attempt` resolves or seals that exact attempt.
Both require `X-Session-Id`, a canonical nonempty UUID held as a private session capability.
Rejected/expired bearer authentication never falls back to guest access. Staff and API credentials
cannot use either route. Current database Customer ownership is checked under row authority.

The strict request contains `schemaVersion`, `operationId`, `tenantId`, `timeZone`, `date`,
`startTime`, `endTime`, `numberOfGuests`, `customerName`, `customerEmail`, `customerPhone` and
`specialRequests`. `date` is a restaurant calendar day; times are restaurant wall times. No table,
status, caller identity, staff note or action-token fields are accepted. Party size is 1–20; contact
and request lengths match the existing reservation columns. The existing customer form settings
control required phone and special requests. Native clients reuse `/api/FormFieldConfiguration`.

New acceptance requires fresh enabled native reservation capability, purchased module, active
lifecycle, known currency, and the exact selected tenant/timezone. Authenticated creation also
requires the current native accounts capability. The existing serving-window engine recomputes
times under the shared tenant-database schedule lock. The server chooses a smallest fitting single
table or enough positive-capacity available tables; combined occupancy is checked again by the
shared booking writer. A new booking remains Pending until restaurant approval.

The shared writer assigns the new server UUID before staging its row. The operation receipt and
booking save and commit in one transaction; mail runs afterward and remains best effort. Schedule
authority precedes the current customer-row guard. Legacy handlers use the same draft rules and
retain their API contracts; they must not be dispatched inside an outer native transaction.

The response is only `{schemaVersion, operationId, outcome, reservationId}`. `Applied` is stable
acceptance, not restaurant confirmation or authority to read private booking details. `Cancelled`
means that unseen create attempt is durably sealed, with null reservationId. Cancel-attempt never
cancels an already applied booking. Exact replay returns its original decision without mail or a
new booking, even after capability/module withdrawal or deletion of the booking. Current account
and original session binding still apply. Another owner receives 404; changing an existing
operation's payload receives 409.

Clients must durably acknowledge the exact scoped private intent before submission and preserve
an unresolved attempt across network loss, process termination and tenant/account changes. Keep
its session and private intent in SecureStore. Do not invent a new operation ID, silently change
its payload, automatically replay offline writes, or claim cancellation from a missing response.
An explicit cancel-attempt request can seal an unseen operation; only its canonical decision
resolves uncertainty. Discard private request details after confirmed acknowledgement according
to the client journal policy. Browser returns are irrelevant to booking acceptance.

Migration `20261020173728_AddCustomerReservationOperations` adds the receipt key, owner/payload
hashes, nullable reservation reference, outcome and UTC completion time. It stores no contacts,
request body, credentials or direct customer identity, and has no booking/user foreign key.
Database constraints reject inconsistent outcomes. Downgrade refuses any nonempty receipt table
under an exclusive lock, including a concurrent insert. No expiry or cleanup removes receipts.
This implementation applies the migration only on a separate disposable local test template;
production/staging migration and release acceptance remain separate.

Both routes use no-store and the existing native request budget. The allocation result is small,
but the legacy engine still scans active tables and that day's bookings. Response limits do not
bound scan/sort work; large schedules, queueing, deployed readback and installed performance remain
rollout gates. Owned native edit/cancellation, the native creation UI/journal, locale review and
installed journeys are separate work.
