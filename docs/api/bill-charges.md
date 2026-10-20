# Staff bill adjustments

Administrators configure up to twelve bill fields at `/api/bill-charges`. A rule has
`effect` (`Increase` or `Reduce`) and `calculation` (`Fixed` or `Percentage`). Omitted
fields retain the legacy `Increase` / `Fixed` behavior. Amounts remain nonnegative
magnitudes; percentage values range from zero to 100. Zero selections are omitted.
Rules keep their order-type, staff-role and custom-amount restrictions.

The admin editor offers a disabled Discount preset. Staff explicitly select a field;
no discount is applied automatically. Custom fields can increase or reduce the bill.
Percentage adjustments use retained item value after earlier reductions and resolved
removals, excluding delivery/service fees and tips. Fields submitted together share
one reviewed item basis. The server rounds once to accepted currency minor units and
freezes each reduction against the retained food-unit ordinals.

Inputs include the reviewed `effect` and `calculation` alongside `key` and `amount`.
A configuration change cannot silently reinterpret an open staff form. Legacy inputs
without these fields are accepted only for legacy fixed increases. Operation replay
compares accepted input semantics and value, retaining the original result after
configuration edits.

Discretionary reductions are applied before captured payments. They cannot create a
refund or change an existing payment allocation. Pending payments, unresolved financial
corrections and legacy allocation-version-zero visits must be reconciled first.
Discounts and percentage adjustments on bills with frozen tax require tax reconciliation.
A zero-value item removal carrying earned/redeemed loyalty points also requires loyalty
reconciliation, rather than silently losing its point ownership.

Accepted JSON evidence is append-only; original item prices and captured tenders remain
intact. Account collection and later amendment credits use net frozen food units. The
current ticket/browser receipt shows the retained discount portions with exact ordinal
rounding. Bill printing stays blocked while a financial correction is unresolved, while amendment
history and Z-report staff discounts preserve the accepted financial evidence. Existing
in-flight refund operations keep the deployed fingerprint contract; new adjustment
records add versioned rule/allocation evidence to their fingerprint.

`OrderDto.billChargeItemBasis` supplies the server-derived eligible item total for staff
previews. `billCharges[].amount` is signed. `payableTotal` remains authoritative; neither
the frontend preview nor a sum of displayed rows reconstructs the payment ledger.

Older admin clients may continue saving fixed increases. Saving an existing discount or percentage rule requires explicit effect and calculation fields; omitted modes return 409 without changing configuration. Refresh the admin page before editing these rules.
