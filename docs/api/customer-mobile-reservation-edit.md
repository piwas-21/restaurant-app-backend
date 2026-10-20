# Native owned reservation edits

This additive source contract lets a current authenticated Customer review and change their own
future Pending or Confirmed reservation. It shares the current owned editor reads and reservation
operation journal. It introduces no migration, configuration activation or deployed contract claim.

| POST route under `/api/customer-mobile/reservations/edit` | Input | Result |
| --- | --- | --- |
| `/prepare` | Full edit input, without hash/proof | Fresh `original` and `proposed` customer projections, `timeZone` and minimal `proof` |
| `/submit` | The exact original full edit input plus returned hash/proof | Canonical schema1 operation resolution |
| `/recover` | Minimal original proof | Canonical resolution or `Unseen`; never applies an edit |
| `/cancel-attempt` | Minimal original proof | Seal an unseen attempt as `Cancelled`, or retain its canonical `Applied` result |

Full input is `schemaVersion=1`, explicit nonempty operation/tenant/reservation UUIDs, exact restaurant
`timeZone`, calendar `date`, wall-clock start/end, party1–20, name/email and optional phone/requests.
Shared creation validation preserves name/email/phone/request limits100/255/20/1000 and current
restaurant requiredness. Unknown fields are refused, including staff status, table, customer ID and
notes. The server owns customer identity, assignment, approval status and audit fields.

All routes require current Customer JWT authority and `X-Session-Id`. A purpose-specific protected
proof binds original Customer/session, tenant, operation, booking database UUID revision, zone,
currency and a hash of the complete exact input. The proof contains no contact input and has no
expiry. Recovery requires the persisted original DataProtection key ring. Creation and cancellation
proofs cannot substitute. A protected original cannot be rebound to another owner, session, operation
or tenant, or resubmitted with changed edit input.

Preparation obtains schedule, booking and Customer authority in the same order as cancellation,
reads current private ownership/policy and computes a fresh proposal. It holds no capacity after
return, writes no booking/receipt and sends no mail. The native customer must review the fresh
original and proposal before explicit confirmation. Contact draft and opaque proof stay outside
routing and Query persistence; durable SecureStore input/proof must be acknowledged before dispatch.
A pending or unknown outcome retains its original operation and blocks a replacement attempt.

Submission obtains the same schedule → booking → Customer locks before effects. Every existing
assigned table is locked in stable ID order, remains active with positive combined capacity and must
be free for the complete offered sitting. A larger spare table cannot replace the original primary
or combined tables. For date/time/party changes the shared engine enforces current serving windows and two-hour sittings,
excluding only this authenticated owner's existing occupancy. Contact-only corrections preserve an
agreed off-grid, longer or formerly offered sitting while rechecking its entire current arrangement
capacity and conflicts. Calendar days are not converted as instants.

The existing guest edit rule applies: a Confirmed booking becomes Pending when its calendar day,
sitting or party size changes; contact-only changes retain confirmation. Primary/combined tables,
booking/customer identity, staff notes, creation audit and frozen mail language are preserved. The
booking and permanent receipt commit atomically. Failure clears tracked pending effects and rolls
back the booking/revision. Fresh owner, tenant, policy, currency/zone, required fields, database
revision, assigned capacity and both original/proposed future starts are checked before commit.

Canonical decisions precede current booking-state/revision/existence checks on replay. Recovery and
attempt stopping remain available after native module withdrawal to the original current Customer;
accounts disabled does not enable a new edit. An applied edit never runs again, undoes later
restaurant changes, sends mail again or changes into a stopped attempt. A stopped attempt changes
no booking. `Unseen` is not permission to discard an unknown attempt or create a replacement.

The existing changed-booking mailer runs only after transaction disposal, retaining guest/operator
language and approval-alert rules. Provider failure cannot undo committed acceptance. Responses use
no-store, contain only schema/operation/outcome/reservation IDs, and share the bounded native read
IP budget. Staff and machine credentials cannot substitute for the owning Customer.

The native editor, complete hosted checks, release/readback, retained protection keys, full locales
and installed process-death/SecureStore/back/accessibility acceptance remain separate gates.
