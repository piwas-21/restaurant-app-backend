# Saved-address write serialization

Create, update, remove and set-default acquire one account-scoped PostgreSQL row lock before
reading or changing addresses, then hold it until the short database transaction completes.
Simultaneous selections therefore cannot each clear the old defaults and commit a different new
one. Removing or editing an address observes the preceding committed write rather than a stale row.

The guard selects only the server-derived owner UUID from the active account. Missing/deleted
accounts are refused, and every address lookup still checks the owner and deletion state. It uses
NO KEY UPDATE so unrelated order foreign-key checks can acquire KEY SHARE. Erasure already obtains
the account lock before deleting saved addresses; these writes join that existing ordering.

No schema change is needed: Users.id/is_deleted are defined by 20250519195300_InitialCreate;
UserAddresses.user_id/is_default/is_deleted are defined by 20250831180232_addAddressesAndOrders.
Existing financial snapshots and wire responses retain their contracts. An explicit default
selection clears other active defaults, including legacy duplicates; this is not a data migration.

The concurrency fixture holds an external account guard and observes two database lock waiters
before releasing simultaneous HTTP writes. It checks each writer, the final default cardinality,
foreign/missing addresses, deleted/missing owners and transaction ownership. Native CRUD controls,
write recovery and installed-device acceptance remain separate delivery work.
