# Native customer reservation reads

Two additive authenticated Customer GETs expose a small native projection:

| Route | Query | Response data |
| --- | --- | --- |
| `/api/customer-mobile/reservations` | Required `schemaVersion=1`; optional complete `beforeDate`, `beforeStartTime`, `beforeId` cursor | `schemaVersion`, `timeZone`, `items`, nullable `next` |
| `/api/customer-mobile/reservations/{reservationId}` | Required `schemaVersion=1` | One item |

Items contain only `reservationId`, `reservationDate` (ISO calendar date), `startTime` and `endTime`
(wall-clock `TimeSpan` strings), `numberOfGuests`, `status` and the customer's `specialRequests`.
The response excludes contact names/email/phone, staff notes, table/floor identities and action
tokens. A reservation day is a stored calendar marker, never an instant converted to the tenant
timezone. The page's `timeZone` identifies the restaurant's wall-clock zone.

History orders by reservation date descending, then start time and ID ascending. It scopes current
database Customer ownership before the SQL limit of 21, returning at most 20 items. A non-null
`next` contains the last visible row's exact UTC `reservationDate` sort marker, `startTime` and `reservationId`; pass all three
as the corresponding `before*` fields without truncating or localizing the timestamp. This preserves
older non-midnight values while item dates remain calendar dates. Partial/malformed cursors and unsupported/missing schema
versions fail with 400. Cursors navigate only within the authenticated owner's scope.

Both reads require the Reservations module and fresh native `reservations=enabled` capability.
Lifecycle read-only/disabled, missing currency and disabled capability refuse the native read.
They also recheck current account existence/Customer role and capability after materialization.
Missing, deleted, guest and foreign reservations have the same 404 detail response. Staff and
machine credentials cannot substitute for the owning customer.

Responses use no-store and the existing native basket read IP budget. This adds no migration or
configuration activation and changes no existing web contract. The deployed OpenAPI fixture is
not evidence that these source-only routes are available on a tenant. Creation and cancellation use their separate durable-operation contracts; owned edit writes and installed native journeys remain separate implementation.
Public availability is specified below.

## Public availability

`GET /api/customer-mobile/reservations/availability?schemaVersion=1&date=YYYY-MM-DD&numberOfGuests=N`
adds a public no-store read using the same native read IP budget. `N` is 1–20, matching the existing
reservation write contract. Date binding is calendar-only; past days are refused against the
restaurant clock, including when UTC has already moved to the next day.

Response data contains only `schemaVersion`, calendar `date`, restaurant `timeZone`, `numberOfGuests`
and at most 48 unique ordered `slots` with `startTime`/`endTime`. The existing shared engine owns
split shifts, elapsed slots and pending/confirmed occupancy across primary and combined tables.
A slot is included only when the available active tables have enough combined capacity for the
requested party. No table IDs, names, layout, notes, QR data or contact fields leave this projection.

A closed day returns an empty slot list. An engine failure or missing active table configuration
returns 400 rather than a successful empty result. Fresh native Reservations capability is required
before and after materialization; withdrawal or a tenant/timezone change refuses a stale response.
Anonymous availability does not authorize reading or changing any reservation.

Availability is advisory and reserves nothing. Creation must revalidate current capacity and conflicts
atomically, bind the original customer/guest authority and give retries a durable operation identity.
Creation uses the separately documented durable-operation contract. Deployed and installed acceptance remain separate gates.


## Private owned editor reads

Two additive Customer-only GETs prepare the current owner to edit a future Pending or Confirmed
booking. Both require `schemaVersion=1`, enabled native accounts and reservations, the Reservations
module, active lifecycle, known currency and the current database Customer role/ownership. A started
or terminal booking returns 409. Missing, guest and foreign bookings return the same 404. Responses
use no-store and the existing shared native read budget; staff/API credentials cannot substitute.

| Route | Additional required query | Response data |
| --- | --- | --- |
| `/api/customer-mobile/reservations/{reservationId}/edit` | None | `schemaVersion`, `tenantId`, `timeZone`, `reservation`, `contact` |
| `/api/customer-mobile/reservations/{reservationId}/edit/availability` | Calendar `date`, `numberOfGuests` 1–20, exact restaurant `timeZone` | `schemaVersion`, `reservationId`, `date`, `timeZone`, `numberOfGuests`, `slots` |

The editor's `reservation` retains the seven-field customer projection above. Only this private
editor returns `contact`: `customerName`, `customerEmail`, nullable `customerPhone`. It never returns
staff notes, primary/combined-table identifiers, QR data, revision, customer ID or audit fields.
History and general details remain contact-free. Native consumers must keep editor contact input
volatile, outside Query persistence, route parameters and durable recovery proofs.

Both reads use fresh scalar database projections and revalidate current ownership, tenant,
capabilities, currency, zone, booking UUID revision and restaurant time after materialization.
Parent or combined-table changes that return to their old values still invalidate the snapshot.
Stored non-midnight calendar markers retain their calendar date without timezone conversion.

Owned availability excludes only this authenticated owner's booking from the shared engine's
occupancy scan. An internal query property supplies that exclusion; public query parameters cannot
bind it. Every table already assigned to the booking must remain active, have positive capacity,
be unoccupied for the entire sitting and collectively fit the requested party. A larger free spare
table cannot silently replace an assigned table. The response contains at most 48 unique ordered
start/end pairs and no table information. Legacy/public callers keep normal occupancy semantics.
Fresh no-tracking table/reservation reads also prevent EF identity-map state from hiding committed
changes. Split shifts and two-hour sittings retain the shared engine's rules.

These are advisory reads: they hold no capacity, create no operation receipt, send no mail and
perform no reservation writes. They introduce no migration. An owned edit still needs a separate
review-bound durable write/recovery contract that rechecks current capacity and revision under
schedule, booking and customer locks, preserves assigned tables, and returns a confirmed booking
to Pending when its date, sitting or party size changes. Contact-only edits retain confirmation.
