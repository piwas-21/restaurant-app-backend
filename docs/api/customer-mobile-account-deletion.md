# Native customer deletion status and requests

The authenticated customer's tenant-local account owns this contract. It accepts no user/account ID.
Customer role, current database ownership and active/non-deleted account are required. Staff and
machine identities cannot use it. These privacy actions reuse the existing account-erasure workflow;
they do not purchase a module or place an order. Native UI and deployed/device acceptance are separate.

## Contract

- `GET /api/customer-mobile/account/deletion` reads the current schedule with no writes.
- `POST /api/customer-mobile/account/deletion/request` accepts only `{ "schemaVersion": 1 }`.
- Both return an API envelope with `schemaVersion: 1`, `state: "not-requested" | "scheduled"`, and
  `scheduledAtUtc: null | UTC timestamp`. Responses are no-store and contain no contact, tokens,
  addresses or foreign-account identifiers.
- Request ingress shares the configured authentication rate budget. Status reads share the native
  basket ingress budget. Unknown fields, an omitted version and unsupported versions are refused.

The original persisted deadline is the recovery evidence. Repeating a request does not move the
30-day deadline or send another email. If persistence fails optimistic concurrency, the caller must
read current status. A lost response does not prove that deletion was not scheduled. A client must
check this status and obtain a new explicit decision before making another request.

Scheduling attempts a confirmation email once. Transport failure does not cancel the stored schedule;
private transport exceptions/token URLs are not logged. This response is not proof of email delivery.
The existing tenant website confirmation link can erase the account earlier. Existing explicit login
cancels a pending schedule; credential refresh does not. The existing cleanup service performs eventual
erasure and retained-record scrubbing. Do not describe scheduling as immediate account deletion.

## Data and integration boundaries

`20250519195300_InitialCreate` defines the Users UUID key, role, deleted marker and Identity concurrency
stamp. `20251210134705_AddDeletionScheduledAtToUser` defines nullable UTC deletion_scheduled_at. No
schema change is added. The server derives identity from authentication and sends the shared scheduling
command in customer-only mode. Existing web calls remain compatible.

Deletion affects only this restaurant account. Financial history is retained under the existing
scrubber/database guards; other restaurant accounts are separate. Physical secure-session cleanup,
complete native account integration, Apple/provider revocation, push-registration removal and durable
email delivery remain required plan work. This contract alone does not complete those integrations.
