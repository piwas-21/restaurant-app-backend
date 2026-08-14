// FILE_LENGTH_EXEMPT: a flat registry of per-policy (limit, window) pairs. Its length is a
// function of how many rate-limited endpoints exist, not of complexity — it was already at
// exactly the 50-line ceiling, so every future policy would hit the same wall. Splitting it
// would buy two IOptions registrations and churn across three appsettings files to make a
// 12-property POCO into two 6-property ones.
using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings;

// Per-IP rate-limit policy values. Production defaults in appsettings.json; Development
// overrides (much higher, so the Playwright E2E suite can run repeatedly from loopback
// without bouncing the API) in appsettings.Development.json. Wired up in Program.cs.
public class RateLimiterSettings
{
    // /api/Auth/login (+ google/apple-login), per IP. Kept tight: with account
    // lockout not yet enforced on the login path, this per-IP window is the only
    // active brute-force throttle, so it must not be loosened casually.
    [Range(1, int.MaxValue)]
    public int AuthPermitLimit { get; set; } = 5;
    [Range(1, int.MaxValue)]
    public int AuthWindowMinutes { get; set; } = 15;

    // /api/Auth/refresh-token, per IP. Separate policy from login so a burst
    // of token refreshes can't drain the login bucket (root cause of admins
    // getting 429 on re-login after a mid-session refresh storm).
    [Range(1, int.MaxValue)]
    public int AuthRefreshPermitLimit { get; set; } = 30;
    [Range(1, int.MaxValue)]
    public int AuthRefreshWindowMinutes { get; set; } = 15;

    // /api/Auth/forgot-password + reset-password, per IP.
    [Range(1, int.MaxValue)]
    public int ForgotPasswordPermitLimit { get; set; } = 3;
    [Range(1, int.MaxValue)]
    public int ForgotPasswordWindowHours { get; set; } = 1;

    // /api/Auth/send-email-verification, per IP. Its OWN partition, for the same reason
    // auth-refresh and checkout-status have theirs: a guest on the restaurant's Wi-Fi who taps
    // "resend" a few times must not 429 the whole venue's NAT out of PASSWORD RESET. Bombing is
    // stopped by the per-address cooldown below, not by this bucket, so this one need not be the
    // tightest in the app — it only has to bound one caller's mail cost.
    [Range(1, int.MaxValue)]
    public int EmailVerificationPermitLimit { get; set; } = 5;
    [Range(1, int.MaxValue)]
    public int EmailVerificationWindowHours { get; set; } = 1;

    // /api/Auth/send-email-verification, per ADDRESS (not per IP) — the policy above caps one
    // caller, this caps how often one inbox can be mailed no matter how many IPs ask, which is
    // the actual email-bombing attack. 0 disables it, which is what Development/E2E use so the
    // Playwright suite can re-register.
    [Range(0, int.MaxValue)]
    public int EmailVerificationCooldownMinutes { get; set; } = 5;

    // /api/User/register/customer, per IP.
    [Range(1, int.MaxValue)]
    public int RegisterPermitLimit { get; set; } = 10;
    [Range(1, int.MaxValue)]
    public int RegisterWindowHours { get; set; } = 1;

    // /api/orders/{orderId}/send-confirmation-email, per IP. Intentionally [AllowAnonymous]
    // for guest checkout (ADR-004); throttling caps SMTP-flood cost from scraped order IDs.
    [Range(1, int.MaxValue)]
    public int ConfirmationEmailPermitLimit { get; set; } = 5;
    [Range(1, int.MaxValue)]
    public int ConfirmationEmailWindowMinutes { get; set; } = 15;

    // /api/Payments/checkout-session, per IP — see the remarks on PaymentsController for why.
    [Range(1, int.MaxValue)]
    public int CheckoutSessionPermitLimit { get; set; } = 10;
    [Range(1, int.MaxValue)]
    public int CheckoutSessionWindowMinutes { get; set; } = 15;

    // /api/Payments/checkout-status, per IP — the diner's return trip from Stripe (S9).
    // DELIBERATELY GENEROUS, ~100x what the frontend needs: it calls once per session id behind a
    // ref guard and never polls. The limit is here to bound an ANONYMOUS caller's amplification of
    // Stripe reads (a session Stripe still reports `open` stays `Created`, so every call re-fetches
    // it), not to police diners — and a false 429 on this endpoint would be shown to someone who
    // has already paid.
    [Range(1, int.MaxValue)]
    public int CheckoutStatusPermitLimit { get; set; } = 120;
    [Range(1, int.MaxValue)]
    public int CheckoutStatusWindowMinutes { get; set; } = 15;
}
