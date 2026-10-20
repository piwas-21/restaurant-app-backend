# Native customer loyalty reads

Both GETs require the authenticated Customer, the Loyalty module, current enabled native `loyalty` capability and `schemaVersion=1`. Machine/staff credentials cannot substitute for the customer. Responses are no-store and use the shared native read budget. The current database account, tenant, currency and capability are checked again after materialization.

| Route | Data |
|---|---|
| `/api/customer-mobile/loyalty/balance` | `schemaVersion`, `currentPoints`, `totalEarnedPoints`, `totalRedeemedPoints`, nullable `lastUpdated` |
| `/api/customer-mobile/loyalty/history` | `schemaVersion`, at most 20 `items`, nullable `next` |

An account without a balance returns zero totals and a null update time. Negative recorded totals refuse with a reconciliation conflict. Recorded points are not a spendability or currency-value quote: outstanding obligations, redemption conflicts and current discounts are recalculated by the existing checkout authority.

A history item contains only `transactionId`, `kind`, signed integer `points`, UTC `createdAt` and nullable UTC `expiresAt`. Kinds are `earned`, `redeemed`, `adjusted`, `expired`, `refunded`, `earned-reversed` and `redemption-restored`; unknown types refuse with a reconciliation conflict. The projection omits staff descriptions, audit identities, order references and financial/source graphs.

History orders by exact creation timestamp descending, then transaction ID ascending. Pass the complete returned `next.createdAt` and `next.transactionId` unchanged as `beforeCreatedAt` and `beforeId`. Ownership is applied before the SQL limit of 21 rows (20 visible plus the next-page sentinel). Missing/partial/malformed cursor values and unsupported/missing schema versions refuse. Empty history has an empty list and null cursor.

These reads write nothing, introduce no migration or settings, and grant no redemption or discount-usage authority. Native discounts, the connected mobile view, deployment and installed acceptance remain separate work.
