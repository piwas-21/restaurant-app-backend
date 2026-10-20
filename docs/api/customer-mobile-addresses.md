# Native customer saved addresses

`GET /api/customer-mobile/account/addresses?schemaVersion=1&afterId=<optional UUID>` reads only the
current authenticated customer's saved addresses. The customer JWT and current non-deleted database
customer must agree. Staff/machine callers and missing/deleted/non-customer records cannot read it.
A cursor is navigation data, never an ownership grant; a query-selected user ID has no authority.

The no-store API envelope contains schemaVersion 1, items and nullable nextAfterId. Items contain
ID, label, two address lines, city, optional state, postal code, country, optional phone, default
marker and optional delivery instructions. No user ID, geolocation, audit fields or tokens are
projected. Treat every item as private customer data.

Pages contain at most 20 items, ordered by UUID ascending. Continue with nextAfterId until null;
accept sparse pages as addresses change. A single no-tracking customer/address projection fetches
at most one extra item to determine continuation. No per-row fetches, count scan or public cache is
required. The owner remains in the same SQL read as the private address rows. An empty list is a
valid customer with no matching addresses; unavailable current customers return 404.

Schema evidence is 20250831180232_addAddressesAndOrders: UUID address/user keys and cascading user
FK; label varchar(50), address lines varchar(200), city/state/country varchar(100), postal/phone
varchar(20), instructions varchar(500), bool default/deleted markers and UTC timestamps. Existing
Users identity/role/deleted fields come from 20250519195300_InitialCreate. No migration is added.
The current-customer query rule is shared with native deletion; existing web address routes remain
compatible. Native address writes reuse their shared validation and transaction guard; checkout
selection and deployed/device acceptance remain in CUSTOMER-MOBILE-APPS-PLAN P3.

## Mutations

POST the collection to create, PUT /{id} to update, POST /{id}/set-default to select a default and
DELETE /{id} to remove. Every request requires schemaVersion 1 and rejects unknown fields. Create
and update additionally require an address object containing exactly the display fields listed
above except ID; isDefault must be an explicit non-null boolean. Required text, Unicode storage
bounds and NUL rejection use the same shared rules as the web writers. The caller cannot supply
ownership, policy flags, coordinates, audit fields or deletion markers.

Create/update return {schemaVersion:1,address:<display item>}. Default/remove return
{schemaVersion:1,addressId:<requested UUID>,state:default|removed}. All responses are no-store.
Native edits preserve unexposed coordinates. Existing web full-update behavior is retained.
Unknown/foreign/deleted targets return 404 without changing either owner's default.

The server checks the current database customer after acquiring its account guard. A role change
or account removal that wins the competing lifecycle lock prevents the write even when the JWT
still claims Customer. Legacy web writers continue to serve their existing authenticated roles.
Server-only command flags are internal and cannot be selected through JSON.

These writes have no automatic retry or token-refresh contract. Create is not idempotent; a lost
response must not trigger a replay. Return to a fresh private address read for explicit checking.
An empty read does not prove an earlier request can never arrive. Do not queue changes offline.
Removing a saved address preserves the separate OrderAddress snapshots on existing orders.
