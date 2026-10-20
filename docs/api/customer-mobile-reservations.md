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
not evidence that these source-only routes are available on a tenant. Reservation creation,
availability, edits/cancellation and installed native journeys remain separate implementation.
