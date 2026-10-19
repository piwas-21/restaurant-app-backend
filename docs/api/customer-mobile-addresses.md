# Native customer saved-address reads

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
unchanged. This slice adds reads only. Native create/edit/remove/default management, checkout
selection and deployed/device acceptance remain in CUSTOMER-MOBILE-APPS-PLAN P3.
