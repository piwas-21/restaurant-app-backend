# Cashier bill printing

Table visits defer the automatic cashier bill until successful session closure. Kitchen routes
continue per order. Closing a nonempty visit creates one durable cashier job containing every
billable round; replaying the close cannot create another job. Empty/all-cancelled visits close
without a bill. Individual counter/takeaway/delivery orders retain their automatic cashier route.

Staff can request additional order or full-visit bills through `POST /api/cashier-print-jobs`
with an operation UUID, exactly one `orderId`/`serviceSessionId`, and `expectedVersion`.
Admin, Cashier and Server roles require the printing module. Retrying the same operation reuses
its immutable financial snapshot; a distinct copy uses a new operation UUID. Financial history,
refund amounts, credits, currency, prices and immutable item identities remain authoritative.

The printer app claims work through `POST /api/cashier-print-jobs/claim`, authenticated with the
tenant API key and installation ID. Claims require a recently reported configured cashier target.
A database row lock assigns a job once across devices. Manual requests ignore auto-print toggles;
automatic close jobs honor both auto-print and quiet hours (`allowAutomatic=false` keeps manual
claims available during restrictions). Jobs expire from claim eligibility after one day.

A claim commits before returning a receipt. A crash or lost response leaves it `Claimed` and
never automatically replays possible output. The owning device reports `Sent`, `Failed` or
`Unknown` to `POST /api/cashier-print-jobs/{id}/complete`. Staff read status through `GET` on the
same job resource. `Sent` means transport acceptance, not confirmed paper. Check the printer
before explicitly requesting another copy after an ambiguous outcome.

Snapshots contain financial/item data without guest contact, bearer tokens or kitchen comments.
Order-specific contact and delivery-address fields are attached only at claim time from the
erasure-aware source order. Table aggregate bills contain no guest contact. Deleting an owning
order/session cascades its print history; schema downgrade refuses to erase saved print jobs.

`acknowledgeCompletedWork` on versioned status/task delivery requests records an audited staff
handover and acknowledges current kitchen corrections. It never fabricates device receipts or
settles payments. Held orders, stale versions, role restrictions and marketplace workflows stay
enforced. Legacy generated line summaries are filtered using exact frozen selection text before
print-language translation, while genuine customer comments remain visible to the kitchen.
