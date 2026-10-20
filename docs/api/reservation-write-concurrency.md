# Reservation booked-slot write concurrency

Creation, staff edits, customer edits, confirmation, cancellation and permanent deletion share
one transaction-scoped PostgreSQL advisory lock per tenant database. Each handler acquires the
lock before its first booking or capacity/overlap read and commits after its last database write.
Early refusals and exceptions dispose the owned transaction. Mail runs after commit.

The coarse schedule lock deliberately covers every booked calendar day. A move does not first
need to read an old day, and two opposite-day moves cannot acquire day locks in opposite order.
This trades parallel write throughput within one restaurant for a simple common authority across
legacy web and future native booking writers. Read-only availability remains advisory and unlocked.
Live dine-in table holds are a separate occupancy system.

Pending and confirmed bookings occupy their primary and combined tables. Confirmation also checks
that complete set before reactivating a no-show; adjacent half-open time intervals do not conflict.
Email quick actions carry their original proof through an internal, non-bindable command property.
After the schedule wait, the handler revalidates that proof against the fresh booking status,
creation time and current signature/legacy clock. A changed, expired or deleted booking uses the
same unusable-link page as an unknown ID. Authenticated staff commands retain their normal path.
Existing ownership, required-field and capacity contracts still apply. This adds no schema,
setting, native booking route, idempotency receipt or tenant activation.

The helper owns a new ReadCommitted transaction. Do not dispatch these legacy commands inside an
outer native-operation transaction: future durable native writes must share the schedule authority
and arrange mail after their final operation commit. Acquire the schedule authority before a
customer row lock: a legacy customer creation holds the schedule while inserting its customer FK.
Taking the opposite order in a native writer could deadlock those two requests. Native writes
must also recheck current customer,
capability, restaurant date/slots and original operation payload before accepting a booking.

Concurrency checks pause the first writer after its real conflict read, observe the second
connection waiting in PostgreSQL pg_locks, then release the first commit. Final booking state is the
oracle: competing create/staff/customer edits cannot both take the same slot. Status/delete cases
observe changes committed while waiting; rollback and cross-connection mail visibility exercise
transaction boundaries. Source checks do not prove deployed or installed native booking acceptance.
