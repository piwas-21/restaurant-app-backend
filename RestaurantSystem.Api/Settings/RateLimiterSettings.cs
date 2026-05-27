using System.ComponentModel.DataAnnotations;

namespace RestaurantSystem.Api.Settings
{
    /// <summary>
    /// Configuration for the per-IP rate-limit policies on the auth endpoints.
    ///
    /// Production defaults live in appsettings.json. Development overrides
    /// (much higher limits, so the Playwright E2E suite can run repeatedly
    /// from loopback without bouncing the API) live in
    /// appsettings.Development.json.
    /// </summary>
    public class RateLimiterSettings
    {
        /// <summary>Permits per AuthWindowMinutes for /api/Auth/login + refresh-token, per IP.</summary>
        [Range(1, int.MaxValue)]
        public int AuthPermitLimit { get; set; } = 5;

        /// <summary>Window length for the auth policy.</summary>
        [Range(1, int.MaxValue)]
        public int AuthWindowMinutes { get; set; } = 15;

        /// <summary>Permits per ForgotPasswordWindowHours for /api/Auth/forgot-password + reset-password, per IP.</summary>
        [Range(1, int.MaxValue)]
        public int ForgotPasswordPermitLimit { get; set; } = 3;

        /// <summary>Window length for the forgot-password policy.</summary>
        [Range(1, int.MaxValue)]
        public int ForgotPasswordWindowHours { get; set; } = 1;

        /// <summary>Permits per RegisterWindowHours for /api/User/register/customer, per IP.</summary>
        [Range(1, int.MaxValue)]
        public int RegisterPermitLimit { get; set; } = 10;

        /// <summary>Window length for the register policy.</summary>
        [Range(1, int.MaxValue)]
        public int RegisterWindowHours { get; set; } = 1;

        /// <summary>
        /// Permits per ConfirmationEmailWindowMinutes for
        /// /api/orders/{orderId}/send-confirmation-email, per IP.
        /// The endpoint is intentionally [AllowAnonymous] to support guest
        /// checkout (see ADR-004). Per-IP throttling caps the cost of an
        /// attacker who has scraped order IDs from receipts/URLs and tries to
        /// flood SMTP.
        /// </summary>
        [Range(1, int.MaxValue)]
        public int ConfirmationEmailPermitLimit { get; set; } = 5;

        /// <summary>Window length for the confirmation-email policy.</summary>
        [Range(1, int.MaxValue)]
        public int ConfirmationEmailWindowMinutes { get; set; } = 15;
    }
}
