# Backend Security Audit

> Audit Date: 2026-04-03 | Risk Level: **CRITICAL** (requires immediate remediation)

---

## Summary

| Severity | Count | Status |
|----------|-------|--------|
| CRITICAL | 5 | Requires immediate fix |
| HIGH | 8 | Fix before production |
| MEDIUM | 10 | Fix soon |
| LOW | 3 | Fix when possible |
| **TOTAL** | **26** | |

---

## CRITICAL Findings

### C1. Password Reset Allows Account Takeover

**File:** `Features/Auth/Commands/ResetPasswordCommand/ResetPasswordCommand.cs`

**Issue:** The `ResetPasswordCommand` does NOT accept or validate the reset token sent to the user's email. Instead, the handler generates a fresh token via `GeneratePasswordResetTokenAsync()` and immediately uses it. This means anyone who knows a user's email can reset their password without the email link.

**Impact:** Complete account takeover for any user.

**Fix:**
```csharp
// BEFORE (vulnerable)
public record ResetPasswordCommand(string Email, string NewPassword) : ICommand<...>;
// Handler generates its own token - bypasses email verification

// AFTER (secure)
public record ResetPasswordCommand(string Email, string Token, string NewPassword) : ICommand<...>;
// Handler validates Token matches what was emailed
```

**Sprint:** Sprint 1.5 (immediate hotfix)

---

### C2. S3 Files Set to PublicRead

**File:** `Common/Services/S3FileStorageService.cs` (line 43)

**Issue:** `CannedACL = S3CannedACL.PublicRead` makes ALL uploaded files (product images, user content) publicly accessible to anyone with the URL, including via URL enumeration.

**Fix:** Use `S3CannedACL.Private` and generate pre-signed URLs with expiration for secure access.

**Sprint:** Sprint 1.5 (immediate)

**Ops follow-up (code done, bucket not yet):** New uploads are now private (`NoACL`). Existing objects still carry `PublicRead`. Run a one-time remediation to close existing exposure:
```bash
# Apply bucket-level Block Public Access (recommended — overrides all object ACLs)
aws s3api put-public-access-block --bucket <bucket-name> \
  --public-access-block-configuration "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"
```

---

### C3. Missing Security Headers

**File:** `Program.cs`

**Issue:** No security headers configured. Missing:
- `Strict-Transport-Security` (HSTS) - no forced HTTPS
- `X-Frame-Options` - no clickjacking protection
- `X-Content-Type-Options` - no MIME sniffing protection
- `Content-Security-Policy` - no XSS/injection protection
- `Referrer-Policy` - referrer leaks possible

**Fix:** Add security headers middleware:
```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "0");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    if (!context.Request.Path.StartsWithSegments("/api/swagger"))
    {
        context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'");
    }
    await next();
});

// In production only:
app.UseHsts();
```

**Sprint:** Sprint 1.5 (immediate)

---

### C4. No Rate Limiting on Authentication Endpoints

**Issue:** Zero rate limiting on:
- `POST /api/auth/login` - unlimited login attempts
- `POST /api/auth/forgot-password` - unlimited password reset emails
- `POST /api/auth/register` - unlimited account creation
- `POST /api/auth/refresh-token` - unlimited token refresh

Account lockout (5 attempts / 15 min) exists but doesn't prevent brute-force at the network level.

**Fix:** Add `Microsoft.AspNetCore.RateLimiting` middleware:
```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(15);
        opt.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("password-reset", opt =>
    {
        opt.PermitLimit = 3;
        opt.Window = TimeSpan.FromHours(1);
        opt.QueueLimit = 0;
    });
});
```

**Sprint:** Sprint 1.5 (immediate)

**Follow-up chore:** `app.UseForwardedHeaders()` is currently placed after `UseHttpsRedirection` and `UseCors`. Move it before both so the forwarded proto is available for redirect decisions (no practical impact today since TLS terminates at ingress, but canonical ordering). Also: `KnownIPNetworks.Clear()` means direct pod access could spoof `X-Forwarded-For` to evade rate limits — acceptable while K8s network policies block direct access, but document if network policies change.

---

### C5. Unvalidated X-Session-Id Header

**File:** `Features/Auth/Commands/LoginCommand/LoginCommand.cs` (line 69)

**Issue:** The `X-Session-Id` header is accepted as-is and used to merge anonymous baskets into authenticated user baskets. An attacker could:
1. Create a basket with expensive items under session A
2. Login as victim with `X-Session-Id: A`
3. Victim's account now has attacker's basket merged

**Fix:** Validate session IDs are proper UUID format and optionally bind to IP/fingerprint.

**Sprint:** Sprint 1.5 (immediate)

---

## HIGH Findings

### H1. SSE Endpoints Missing Authorization

**File:** `Features/Orders/EventsController.cs`

**Issue:** Three SSE endpoints have NO authorization:
- `GET /api/events/kitchen` - Exposes all kitchen orders
- `GET /api/events/stock` - Exposes stock updates
- `GET /api/events/service` - Exposes service events

**Fix:** Add `[Authorize(Roles = "Admin,KitchenStaff")]` to kitchen, `[Authorize]` to stock/service.

---

### H2. Test Broadcast Endpoint Not Protected

**File:** `Features/Orders/EventsController.cs` (line 75)

**Issue:** `POST /api/events/test-broadcast` can be called by any authenticated user to broadcast fake orders to all connected clients.

**Fix:** Add `[RequireAdmin]` attribute.

---

### H3. RequireHttpsMetadata = false

**File:** `Program.cs` (line 170)

**Issue:** `options.RequireHttpsMetadata = false` allows JWT validation over HTTP. This setting applies to ALL environments.

**Fix:** Make environment-specific:
```csharp
options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
```

---

### H4. Password Reset Token Logged in Plaintext

**File:** `Features/Auth/Commands/ForgotPasswordCommand/ForgotPasswordCommand.cs` (line 42)

**Issue:** `_logger.LogInformation("Password reset token for {Email}: {Token}", command.Email, token)` exposes reset tokens in logs.

**Fix:** Remove this log line entirely, or log only a hash for debugging.

---

### H5. Many Commands Missing Validators

**Missing validators for:**
- `RemoveFromBasketCommand`, `ClearBasketCommand`
- `UpdateProductCommand`, `DeleteProductCommand`
- `UploadProductImageCommand`, `DeleteProductImageCommand`, `UpdateProductImageCommand`
- `GoogleLoginCommand`, `AppleLoginCommand`
- `SetFeaturedSpecialCommand`, `UnsetFeaturedSpecialCommand`
- Most reservation, category, and menu commands

**Fix:** Create FluentValidation validators for ALL commands.

---

### H6. SSE Connection Limit Not Enforced

**Issue:** No maximum connections per IP or per user for SSE endpoints. An attacker could open thousands of connections, exhausting server resources.

**Fix:** Add connection counting per IP in `SseClientManager`, reject new connections above threshold (e.g., 10 per IP).

---

### H7. Refresh Token Stored as Plaintext

**File:** `ApplicationUser.RefreshToken` property

**Issue:** Refresh tokens stored in plaintext in the database. If DB is breached, all tokens are exposed.

**Fix:** Hash refresh tokens (like passwords) and store only the hash.

---

### H8. No Session Invalidation on Password Change

**Issue:** When a user changes their password, existing JWT and refresh tokens remain valid until natural expiration.

**Fix:** Invalidate refresh token on password change. Add `SecurityStamp` validation to JWT.

---

## MEDIUM Findings

### M1. Password Policy Inconsistency
ChangePasswordCommand validator requires min 6 chars, but Identity config requires 8. **Fix:** Align to 8.

### M2. Refresh Token Not Invalidated on Logout
No logout endpoint exists to invalidate refresh tokens.

### M3. Local File Storage Path Traversal Risk
`LocalFileStorageService` accepts `folder` parameter without sanitization.

### M4. CORS Empty in Production Falls Back to AllowAll
If `CorsSettings:AllowedOrigins` is empty or missing, defaults to AllowAnyOrigin.

### M5. GlobalIngredients Controller Direct DbContext
Bypasses CQRS pattern, uses `CreatedBy = "System"` instead of `GetAuditIdentifier()`.

### M6. HTML Templates in Controllers
OrdersController and ReservationsController contain 100+ lines of inline HTML (XSS risk if user input reflected).

### M7. File Upload Size Not Enforced at Middleware Level
Size limit in `FileStorageSettings` but no Kestrel `MaxRequestBodySize` configured.

### M8. Email Enumeration via Register
Registration endpoint returns different messages for existing vs new emails.

### M9. No HTTPS Redirect in Production
`UseHttpsRedirection()` exists but without HSTS, browsers may not enforce.

### M10. Missing Content-Type Validation on File Uploads
File extension validated but Content-Type header not cross-checked.

---

## LOW Findings

### L1. Emoji in Log Messages
`_logger.LogInformation("🖨️ Printer feed request...")` - emojis can cause issues with some log aggregators.

### L2. Health Check Exposes Service Name
`/health` endpoint returns `service = "restaurant-system-api"` which aids reconnaissance.

### L3. Swagger Available in All Environments
Swagger UI is not restricted to development environment.

---

## Remediation Priority

### Immediate (Sprint 1.5 - This Week)
1. Fix password reset command (C1)
2. Fix S3 public read (C2)
3. Add security headers (C3)
4. Add rate limiting (C4)
5. Validate X-Session-Id (C5)
6. Protect SSE endpoints (H1, H2)

### Before Next Release
7. Fix RequireHttpsMetadata (H3)
8. Remove token logging (H4)
9. Add missing validators (H5)
10. Add SSE connection limits (H6)

### Next Sprint
11. Hash refresh tokens (H7)
12. Invalidate sessions on password change (H8)
13. Fix all MEDIUM issues (M1-M10)

---

## Security Testing Checklist

After each fix, verify:
- [ ] `POST /api/auth/forgot-password` + `POST /api/auth/reset-password` requires valid token from email
- [ ] S3 URLs require pre-signed access (direct URL returns 403)
- [ ] Security headers present in all responses (check via `curl -I`)
- [ ] Rate limiter blocks after 5 rapid login attempts
- [ ] SSE endpoints return 401 for unauthenticated requests
- [ ] `X-Session-Id` only accepts valid UUID format
- [ ] File uploads rejected above configured size limit
- [ ] Password change invalidates existing tokens
