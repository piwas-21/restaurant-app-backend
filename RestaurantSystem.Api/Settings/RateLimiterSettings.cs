using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings
{
    // Per-IP rate-limit policy values. Production defaults in appsettings.json;
    // Development overrides (much higher, so the Playwright E2E suite can run
    // repeatedly from loopback without bouncing the API) in
    // appsettings.Development.json. Wired up in Program.cs.
    public class RateLimiterSettings
    {
        // /api/Auth/login + refresh-token, per IP.
        [Range(1, int.MaxValue)]
        public int AuthPermitLimit { get; set; } = 5;
        [Range(1, int.MaxValue)]
        public int AuthWindowMinutes { get; set; } = 15;

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
