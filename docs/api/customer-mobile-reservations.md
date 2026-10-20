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
not evidence that these source-only routes are available on a tenant. Reservation creation, edits/cancellation and installed native journeys remain separate implementation.
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
That write contract and the dependent native availability UI are separate implementation gates.
