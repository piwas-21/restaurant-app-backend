# Cashier order groups

`GET /api/orders/cashier-groups` is a staff-only, cashier-module read. It uses the same
filters as `GET /api/orders`, but pages groups rather than individual orders. A group is
one open table service visit (including a released, still-payable visit), or one order
without an open visit. Closed visits are not combined. Customer names and table labels
are never used to infer membership. Every order keeps its original ID and order number.

The response is `ApiResponse<PagedResult<CashierOrderGroupDto>>`. Each item contains:

- `groupKey`: `visit:<uuid>` or `order:<uuid>`; stable presentation identity.
- `serviceSessionId`: the open visit UUID, or null for a standalone row.
- `tableNumber`: the first member's numeric compatibility value, or null.
- `releasedAt`: the visit's release time, or null. Released visits remain collectible.
- `isArchivedFromTable`: a recovered legacy order belongs to a prior occupancy, not the current guests.
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
