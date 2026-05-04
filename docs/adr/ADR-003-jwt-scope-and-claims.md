# ADR-003 — JWT scope and claim shape

**Status:** Accepted
**Date:** 2026-04-26
**Author:** mahmutkaya
**Reviewers:** —
**Implements / supersedes:** —
**References:**
- [RestaurantSystem.Api/Settings/JwtSettings.cs](../../RestaurantSystem.Api/Settings/JwtSettings.cs)
- [RestaurantSystem.Api/Program.cs](../../RestaurantSystem.Api/Program.cs) — `AddJwtBearer`
- [RestaurantSystem.Api/Common/Authorization/Attributes.cs](../../RestaurantSystem.Api/Common/Authorization/Attributes.cs)
- [RestaurantSystem.Api/Features/Auth/](../../RestaurantSystem.Api/Features/Auth/) — token issuance

---

## Context

A single-restaurant deployment authenticates three actor classes:

1. **Customer** — places orders, manages own profile.
2. **Cashier** — operates the in-store cashier UI; sees today's orders, takes payments.
3. **Admin** — full restaurant management: menus, settings, staff, financial reports.

Plus internal automation:

4. **Printer-app** — polls the printer-feed endpoint (today: open; future: API-key auth — see follow-up issue #1).

We need an auth token that:
- Is **stateless** at the API tier (no per-request DB hit for "who are you?"). Identity provider is the JWT itself.
- Carries enough to make routing/authorization decisions without re-fetching the user.
- Is **short-lived** with refresh, so revocation latency is bounded.
- **Doesn't carry sensitive PII** beyond identifier + role, in case logs leak.

## Decision

### Token format

Standard JWT (`Bearer`), HS256-signed with `JwtSettings.Secret` (≥32 bytes), issued by `JwtSettings.Issuer`, audience `JwtSettings.Audience`.

### Claims (minimum viable set)

| Claim | Source | Purpose |
|---|---|---|
| `sub` | `ApplicationUser.Id` (Guid → string) | Subject identifier |
| `email` | `ApplicationUser.Email` | Display in audit logs; never used for authorization |
| `role` | `ApplicationUser.Role` (enum → string) | Authorization decisions: `Customer` / `Cashier` / `Admin` |
| `jti` | `Guid.NewGuid()` | Unique token identifier (for future revocation) |
| `iat` | issue time | Standard |
| `exp` | issue time + `ExpirationMinutes` | Standard |

**Excluded from claims** (intentional):
- `password_hash` — never. Even though it's hashed, it's irrelevant to the API tier.
- `phone`, `address` — fetch from DB on demand. Tokens leak in browser-history, server logs, mobile-app crash reports; minimise PII surface.
- Per-feature permissions — role is sufficient for current authorization model. If we ever need finer-grained scoping, we add a `permissions` claim, not refactor every existing claim.

### Lifetimes

- **Access token:** `JwtSettings.ExpirationMinutes` minutes (default: 60).
- **Refresh token:** `JwtSettings.RefreshTokenExpirationDays` days (default: 7), stored server-side (DB) so it can be revoked.

### Authorization

`[Authorize]` for any-role auth; role-specific via custom attributes:
- `[RequireAdmin]`
- `[RequireAdminOrCashier]`
- `[RequireCashier]`

These wrap `RequireRoleAttribute` for clarity at the callsite.

## Consequences

### Positive
- **Stateless API tier** — no DB call to authenticate a request.
- **Small token** — fits comfortably in `Authorization` header; doesn't bloat every request.
- **PII-minimal** — email is the only PII; nothing sensitive that would meaningfully harm a user if a token leaks.
- **Role check is fast** — string compare on a claim, not a DB join.

### Negative
- **Revocation latency = token TTL.** A compromised access token is valid until expiry. Mitigated by short lifetime (60 min) + refresh-token revocation in DB.
- **Role changes don't take effect until token refresh.** If admin demotes a user mid-session, that user retains admin permissions for up to 60 min. Acceptable for current trust model; would need tighter control if we add high-risk operations.
- **No per-tenant claim.** Today there's only one restaurant. If RUMI becomes SaaS multi-tenant, every existing token + every authorization check needs a tenant claim. Flagged as a SaaS-prerequisite ADR.
- **Email in a claim** — if a user changes their email, old tokens still show the old email until refresh. Display-only impact; not a security issue.

### Mitigation for the negatives
- Document the 60-min revocation latency in the security audit doc.
- For privileged operations (financial reports, account deletion), consider an additional confirmation step (re-enter password) so the auth check isn't just "did you have a valid token at the start of this session?".

## Alternatives considered

### Alternative A: Opaque tokens (DB-backed)
Each token is a random opaque string that requires a DB lookup per request to find the associated session. Stronger revocation (drop the DB row, token dies instantly), but adds a per-request DB call. Rejected because the cost is too high for the gain at our scale; revocation latency is acceptable.

### Alternative B: Asymmetric signing (RS256)
Public-key verification would let us share tokens with a separate verifier service without sharing the signing secret. We have no separate verifier today; the symmetric secret is fine. Re-evaluate if we add an external service that needs to verify tokens.

### Alternative C: Embedding more user state in the token (FullName, Phone, Address)
Saves DB lookups for read-mostly UI views (e.g. show the user's name in the header). Rejected because (a) any change in user data is invisible until token refresh, (b) PII surface grows — a leaked log line now exposes more, (c) tokens get larger.
