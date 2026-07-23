using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings
{
    // Per-IP rate-limit policy values. Production defaults in appsettings.json;
    // Development overrides (much higher, so the Playwright E2E suite can run
    // repeatedly from loopback without bouncing the API) in
    // appsettings.Development.json. Wired up in Program.cs.
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

        // /api/User/register/customer, per IP.
        [Range(1, int.MaxValue)]
        public int RegisterPermitLimit { get; set; } = 10;
        [Range(1, int.MaxValue)]
        public int RegisterWindowHours { get; set; } = 1;

        // /api/orders/{orderId}/send-confirmation-email, per IP.
        // Endpoint is intentionally [AllowAnonymous] for guest checkout (ADR-004);
        // throttling caps SMTP-flood cost from scraped order IDs.
        [Range(1, int.MaxValue)]
        public int ConfirmationEmailPermitLimit { get; set; } = 5;
        [Range(1, int.MaxValue)]
        public int ConfirmationEmailWindowMinutes { get; set; } = 15;
    }
}
