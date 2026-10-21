# Reservation booked-slot write concurrency

Creation, staff edits, customer edits, confirmation, cancellation and permanent deletion share
one transaction-scoped PostgreSQL advisory lock per tenant database. Each handler acquires the
lock before its first booking or capacity/overlap read and commits after its last database write.
Early refusals and exceptions dispose the owned transaction. Mail runs after commit.

The coarse schedule lock deliberately covers every booked calendar day. A move does not first
need to read an old day, and two opposite-day moves cannot acquire day locks in opposite order.
This trades parallel write throughput within one restaurant for a simple common authority across
legacy web and native booking writers. Read-only availability remains advisory and unlocked.
Live dine-in table holds are a separate occupancy system.

Pending and confirmed bookings occupy their primary and combined tables. Confirmation also checks
that complete set before reactivating a no-show; adjacent half-open time intervals do not conflict.
Email quick actions carry their original proof through an internal, non-bindable command property.
After the schedule wait, the handler revalidates that proof against the fresh booking status,
creation time and current signature/legacy clock. A changed, expired or deleted booking uses the
same unusable-link page as an unknown ID. Authenticated staff commands retain their normal path.
Existing ownership, required-field and capacity contracts still apply. The shared lock helper
adds no schema or setting. Native creation and cancellation use separate additive contracts.

The helper owns a new ReadCommitted transaction. Do not dispatch these legacy commands inside an
outer native-operation transaction: future durable native writes must share the schedule authority
and arrange mail after their final operation commit. New creation acquires schedule authority before a
customer row lock: a legacy customer creation holds the schedule while inserting its customer FK.
Taking the opposite order in a native writer could deadlock those two requests. Native writes
must also recheck current customer, capability, restaurant date/slots and original operation payload.

For an existing owned booking, native cancellation locks schedule → booking → Customer. The real
account eraser scrubs retained booking rows before deleting the Customer. Taking Customer before
booking would invert that order and can deadlock. Canonical operation decisions are read before
booking status/revision checks, so replay never cancels a reopened booking. See
[owned cancellation](customer-mobile-reservation-cancellation.md) for UUID revision triggers,
permanent recovery and migration downgrade protection.

Concurrency checks pause the first writer after its real conflict read, observe the second
connection waiting in PostgreSQL pg_locks, then release the first commit. Final booking state is the
oracle: competing create/staff/customer edits cannot both take the same slot. Status/delete cases
observe changes committed while waiting; rollback and cross-connection mail visibility exercise
transaction boundaries. Source checks do not prove deployed or installed native booking acceptance.


Email actions additionally bind the actual saved database UUID revision. The original version2
proof is verified before and after waiting; status or parent/child ABA cannot revive a retired
button. Writers refresh tracked mail snapshots after all parent/child effects before committing.
Pending customer contact/no-op corrections send fresh operator authority; Confirmed contact-only
corrections remain quiet. Unsigned/version1 links cannot prove a revision and use dashboard recovery.
