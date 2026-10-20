# Saved-address input validation

Create and update inherit one address-content record and use the same validation rules before their existing owner-scoped handlers.
The storage contract is `20250831180232_addAddressesAndOrders` plus `UserAddressConfiguration`:

| Field | Required | Maximum Unicode characters |
| --- | --- | --- |
| Label | Yes | 50 |
| AddressLine1 | Yes | 200 |
| AddressLine2 | No | 200 |
| City | Yes | 100 |
| State | No | 100 |
| PostalCode | Yes | 20 |
| Country | Yes | 100 |
| Phone | No | 20 |
| DeliveryInstructions | No | 500 |

Optional text may be null. Supplementary Unicode characters count once, matching PostgreSQL text
bounds; UTF-16 code-unit counts would incorrectly reject valid labels. NUL cannot be stored in
PostgreSQL text and is rejected before persistence. Required fields continue to reject whitespace.

No migration or wire field changes are required. This tightens invalid input handling on the existing
web create/update routes: overflows return validation errors rather than reaching a database failure.
Native writers should implement these same verified bounds when connected. This change does not
establish default-address concurrency, native write recovery or installed-device acceptance.

`AddressFieldValidationTests` exercises both HTTP writers, exact-limit PostgreSQL persistence,
per-field overflow rejection without row changes, requiredness, nullable fields and Unicode/NUL cases.

The optional isDefault input is nullable and resolves omitted/null to false before persistence;
stored and response defaults remain non-null booleans. Existing omitted-field behavior is preserved.
