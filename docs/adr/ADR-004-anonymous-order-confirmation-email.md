# ADR-004 — Anonymous access on `POST /api/orders/{orderId}/send-confirmation-email`

**Status:** Accepted
**Date:** 2026-05-27
**Author:** mahmutkaya
**Reviewers:** —
**Implements / supersedes:** closes [#2](https://github.com/piwas-21/restaurant-app-backend/issues/2)
**References:**
- [RestaurantSystem.Api/Features/Orders/OrderEmailController.cs](../../RestaurantSystem.Api/Features/Orders/OrderEmailController.cs)
- [RestaurantSystem.Api/Program.cs](../../RestaurantSystem.Api/Program.cs) — `"confirmation-email"` policy
- [RestaurantSystem.Api/Settings/RateLimiterSettings.cs](../../RestaurantSystem.Api/Settings/RateLimiterSettings.cs)
- `frontend/src/services/emailService.ts` — caller (`requireAuth: false`)
- `frontend/src/app/checkout/review/page.tsx:309` — invocation site

---

## Context

`POST /api/orders/{orderId}/send-confirmation-email` is decorated `[AllowAnonymous]`. The endpoint:

1. Looks up the order by GUID.
2. Sends a confirmation email to the customer address recorded on the order.
3. Sends a fire-and-forget admin-notification email.

The frontend calls it from the checkout review page **immediately after order creation**, with `requireAuth: false`. The site supports **guest checkout** for takeaway and delivery — at that point the customer has no JWT to present. Dine-in orders bypass this endpoint entirely (the confirmation email is sent synchronously inside `CreateOrderCommandHandler`, line 159).

Issue [#2](https://github.com/piwas-21/restaurant-app-backend/issues/2) flagged the anonymous surface and asked for an explicit decision: lock down, keep anonymous + mitigate, or middle path (signed token).

### Threat model

| Attack | Feasibility | Impact |
|---|---|---|
| Brute-force a valid `orderId` (128-bit GUID space) | Practically infeasible | n/a |
| Replay against a known/scraped `orderId` (URL leaks, receipt PDF, support thread) | Plausible | Customer-inbox spam; admin-inbox spam → SMTP cost |
| Read order data from the HTTP response | Not exploitable | The handler returns only a success string. Order details flow exclusively to addresses already recorded on the order — not a data-disclosure vector. |
| Enumerate addresses of customers | Not exploitable | The customer address is not in the response; the attacker must already know the order to trigger an email to its recorded address. |

The realistic abuse is **replay-driven spam and SMTP-cost inflation**, not data exfiltration.

## Decision

We will **keep `[AllowAnonymous]`** on this endpoint and mitigate the replay-abuse surface with a **per-IP rate limit** (`"confirmation-email"` policy, 5 requests / 15 minutes / IP in production), matching the pattern already in use for `auth`, `forgot-password`, and `register`.

The XML doc comment on the action method states the threat surface and the rationale inline, so a reader does not need to find this ADR to understand why the attribute is correct.

## Consequences

### Positive
- Guest checkout continues to work end-to-end. The takeaway/delivery confirmation flow does not require the frontend to first authenticate a throwaway session.
- The mitigation reuses the existing `Microsoft.AspNetCore.RateLimiting` middleware and settings layer. No new infrastructure, no new dependencies, no schema migration.
- Per-IP partitioning honours `X-Forwarded-For` (already configured in `Program.cs` for the K8s ingress) — limits are enforced against real client IPs, not the ingress.

### Negative
- IP-based limits punish NAT-shared clients (e.g. corporate networks): a determined attacker on a shared IP can also temporarily throttle a legitimate customer behind the same egress. Acceptable: the cost is a delayed re-send, not a lost order.
- Fixed-window (not sliding) — a burst across two adjacent windows can reach 2× the nominal limit. Matches the precedent set by the existing policies; not worth diverging.

### Mitigation for the negatives
- Limits are configuration-driven (`RateLimiter:ConfirmationEmail*`) so production can tighten without a code change if abuse is observed.
- The admin-notification email is already `Task.Run` fire-and-forget — failures don't surface to the caller, so retries don't compound the cost.

## Alternatives considered

### Alternative A: Require auth (`[RequireAdminOrCashier]` or any authenticated user)
Rejected. Would break guest checkout, which is the dominant path on the customer-facing site. The cashier-only model proposed in issue #2 assumed an internal use; the actual call site is the public checkout flow, so locking it down breaks the product.

### Alternative B: Signed-token URL (HMAC over `orderId` + timestamp, issued at order creation)
A valid middle path. Rejected for **now** as over-engineering: it adds a token-issuance + verification surface for a threat (SMTP-cost inflation) that the per-IP rate limit already caps tightly. Revisit if rate-limit metrics show real-world abuse that IP partitioning can't contain.

### Alternative C: Per-order send-once cooldown (`Order.LastConfirmationEmailSentAt` column, 24h cooldown)
Rejected. Requires an EF migration on the `Orders` table — the costliest entity in the system to migrate — for a benefit (per-order throttle) already covered by per-IP throttling against the realistic attacker. If a single attacker holds many IPs and many order IDs, the cooldown helps; but at that scale the SMTP provider's own per-recipient throttle is the right defence, not the application DB.
