# Cashier order groups

`GET /api/orders/cashier-groups` is a staff-only, cashier-module read. It uses the same
filters as `GET /api/orders`, but pages groups rather than individual orders. A group is
one table service visit (including closed and released visits), or one original order
with all its amendment dispatches. Customer names and table labels
are never used to infer membership. Every order keeps its original ID and order number.

The response is `ApiResponse<PagedResult<CashierOrderGroupDto>>`. Each item contains:

- `groupKey`: `visit:<uuid>` or `order:<uuid>`; stable presentation identity.
- `serviceSessionId`: the visit UUID, or null for a standalone row.
- `tableNumber`: the first member's numeric compatibility value, or null.
- `releasedAt`: the visit's release time, or null. Released visits remain collectible.
- `isArchivedFromTable`: a recovered legacy order belongs to a prior occupancy, not the current guests.
- `hasUpdates`: later rounds or a committed amendment exist, including updates without additions.
- `orders`: complete member `OrderDto` rows, oldest first with ID as a tie-breaker.

Filters select a group when at least one member matches. Its full membership remains in
the returned group, including earlier or cancelled rounds, so a search or page boundary
cannot hide part of the account. The collection account endpoint remains authoritative
for the payable balance; adding order amounts does not reconstruct an allocation ledger.

`page` and `pageSize` count groups. Page size is 1–100; an outdated page beyond the last
group is clamped to the current last page. Groups sort by the latest matching order date,
with a stable ID tie-breaker (`orderBy=OrderDate`, `descending` defaults to true). Counts,
page identities and children share one PostgreSQL repeatable-read snapshot. The endpoint
requires a complete refresh and refuses `modifiedSince` and `syncCursor`. The existing
order synchronization feed remains unchanged for operational notifications.

Collecting from a group navigates to `/cashier/collection?serviceSessionId=<uuid>`.
Individual order actions keep their order context; an order with visit membership must
collect against the visit account rather than `POST /api/orders/{id}/payments`.

Cashier renders one row per group. Later rounds and amendment dispatches remain accessible
inside Amendment history, including when new amendments are disabled or their history read
fails. The Orders navigation badge asks for the server's tenant day and counts today's groups;
archived days are excluded. Operational preparation and payment identities remain separate.

`orders.amendment_root_order_id` records the original standalone order for every supplement.
The migration backfills committed historical chains without rewriting orders or payments.

`GET /api/orders/{orderId}/current-ticket` reads the complete selected visit or standalone
amendment chain for the current ticket. It requires staff authorization and either the
cashier or server module. Its `ApiResponse<IReadOnlyList<OrderDto>>` uses a repeatable-read
snapshot, retains current server totals, payments, versions and permitted actions, and
applies committed removals, replacements and preparation-instruction edits to each member's
item view. Replacement/addition items belong to their supplement and appear once. Original
order items and financial records remain intact. This operational read includes staff
preparation text; the separate audit-history response continues to redact free text.

The default interactive ticket budget is 100 member orders and 1,000 committed amendments
per group. Operators can configure `CurrentStaffTicket:MaximumMembers` (1–1,000) and
`CurrentStaffTicket:MaximumAmendments` (1–10,000), or the corresponding double-underscore
environment keys. Options validation rejects invalid budgets at startup. A bounded
preflight reads at most one entry beyond each configured budget before loading the
order/payment graph. An oversized group returns HTTP 409 with an explicit refusal; it
never returns a partial ticket or bill. Both checks share the ticket's repeatable-read
snapshot, so concurrent amendments cannot evade the history budget or yield mixed totals.

Current-ticket monetary presentation retains only the discount portions belonging to the
remaining food units. Subtotals and original sale discounts retain exact ordinal penny
ownership; original discount categories combine into the current Discount line. Pending
financial corrections hold bill printing until credit/refund/loyalty resolution. The stored
original amounts, credit journal, payment balance and eligible adjustment basis remain
authoritative and unchanged. See [staff bill adjustments](bill-charges.md).

The cashier's main ticket and browser bill display the combined active members from this
snapshot. Amendment history is a compact change log with detailed snapshots and recorded
financial effects available on expansion. Staff mutations continue to target individual
order IDs/versions, and table payment collection continues to use the authoritative visit
account endpoint.
