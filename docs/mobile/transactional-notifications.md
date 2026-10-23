# Transactional notification capture

This prerequisite adds a tenant-local, subscription-gated outbox. Internal watch records contain
only an exact order/reservation kind and UUID. There is no public enrollment endpoint yet: this
migration installs no watches and captures no existing history. Future subscription handlers must
prove current customer ownership or the exact guest-order capability before creating a watch.
A watch is capture interest, never authorization to read or dispatch.

Database triggers capture watched order status/payment/preparation/delivery changes and watched
reservation status/day/time/party changes, including direct SQL and EF ExecuteUpdate writes.
Unchanged values, contact erasure, staff notes and revision-only updates produce no hints.
Each event is an opaque refresh hint, with a database UUID deduplication key and UTC timestamp.
It is not a status or financial snapshot. No credentials, contact fields, prices, device tokens
or destination URLs enter the queue.

Capture is in the business transaction. Rollbacks remove both changes. Watch withdrawal cascades
pending hints; subject hard deletion and order soft deletion withdraw the watch. A row lock before
the FK check serializes concurrent withdrawal. Dispatch must independently revalidate tenant,
subscription consent and subject authority, then fetch current state after an authorized tap.

No worker, credential, provider I/O, push permission prompt or delivery claim is part of this PR.
The next contract adds authorized durable subscription enrollment/withdrawal and tenant-scoped
shared-service delivery. Expo receipts/retries/invalid-token expiry and native lifecycle/taps follow.
Processor/privacy inventory and delivery expiry/purge policy must be complete before production use.
