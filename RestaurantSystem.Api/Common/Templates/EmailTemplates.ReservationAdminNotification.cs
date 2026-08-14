using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Reservation admin notification email template with approve/reject actions
    /// </summary>
    public static class ReservationAdminNotification
    {
        private const string Set = "ReservationAdminNotification";

        /// <summary>The mail's own title, rendered in the head, in both colour schemes and in the text body.</summary>
        private const string HeadingKey = "Heading";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, Guid reservationId, string customerName, string customerEmail, string customerPhone,
            DateTime reservationDate, TimeSpan startTime, TimeSpan endTime, int numberOfGuests, string tableNumber,
            string baseUrl, string frontendBaseUrl, string contactEmail,
            string? specialRequests = null)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var apiBaseUrl = baseUrl;
            var frontendUrl = frontendBaseUrl;

            var requestsSection = string.IsNullOrEmpty(specialRequests)
                ? ""
                : $@"<div style='background: #fef3c7; border-left: 4px solid #f59e0b; padding: 16px; margin: 20px 0; border-radius: 8px;'>
                        <strong style='color: #92400e; font-size: 14px;'>📝 {t["SpecialRequestsLabel"]}</strong><br>
                        <span style='color: #78350f; margin-top: 8px; display: block; white-space: pre-line;'>{EmailHtml.Encode(specialRequests)}</span>
                    </div>";

            var formattedDate = reservationDate.ToString("dddd, MMMM dd, yyyy");
            var formattedStartTime = startTime.ToString(@"hh\:mm");
            var formattedEndTime = endTime.ToString(@"hh\:mm");

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <meta name='color-scheme' content='light dark'>
    <title>{t[HeadingKey]}</title>
    <style>
        @media (prefers-color-scheme: dark) {{
            .light-only {{ display: none !important; }}
            .dark-only {{ display: block !important; }}
        }}
        @media (prefers-color-scheme: light) {{
            .dark-only {{ display: none !important; }}
            .light-only {{ display: block !important; }}
        }}
    </style>
</head>
<body style='margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif; line-height: 1.6; background-color: #f3f4f6;'>
    <!-- Light Mode Version -->
    <div class='light-only' style='max-width: 600px; margin: 0 auto; background: #ffffff;'>
        <!-- Header -->
        <div style='background: linear-gradient(135deg, #d4af37 0%, #f4c430 100%); padding: 32px 24px; text-align: center;'>
            <h1 style='margin: 0; color: #ffffff; font-size: 28px; font-weight: 700;'>🍽️ {brand.Name}</h1>
            <p style='margin: 8px 0 0 0; color: rgba(255,255,255,0.9); font-size: 14px;'>{t[HeadingKey]}</p>
        </div>

        <!-- Content -->
        <div style='padding: 32px 24px;'>
            <!-- Reservation ID Badge -->
            <div style='background: linear-gradient(135deg, #8b5cf6 0%, #7c3aed 100%); color: white; padding: 24px; border-radius: 12px; text-align: center; margin-bottom: 24px; box-shadow: 0 4px 6px rgba(139, 92, 246, 0.2);'>
                <div style='font-size: 12px; text-transform: uppercase; letter-spacing: 1px; opacity: 0.9; margin-bottom: 4px;'>{t["ReservationIdLabel"]}</div>
                <div style='font-size: 24px; font-weight: 700; letter-spacing: 1px;'>{reservationId}</div>
            </div>

            <!-- Customer Info -->
            <div style='background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #111827; font-size: 16px; font-weight: 600;'>👤 {t["CustomerInfoTitle"]}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px; width: 80px;'>{t["NameLabel"]}</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px; font-weight: 500;'>{EmailHtml.Encode(customerName)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px;'>{t["EmailLabel"]}</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px;'>{EmailHtml.Encode(customerEmail)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px;'>{t["PhoneLabel"]}</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px;'>{EmailHtml.Encode(customerPhone)}</td>
                    </tr>
                </table>
            </div>

            <!-- Reservation Details -->
            <div style='background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #111827; font-size: 16px; font-weight: 600;'>📅 {t["DetailsTitle"]}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px; width: 80px;'>{t["DateLabel"]}</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px; font-weight: 500;'>{formattedDate}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px;'>{t["TimeLabel"]}</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px; font-weight: 500;'>{formattedStartTime} - {formattedEndTime}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px;'>{t["GuestsLabel"]}</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px; font-weight: 500;'>{t.Format("GuestCount", numberOfGuests)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #6b7280; font-size: 14px;'>{t["TableLabel"]}</td>
                        <td style='padding: 6px 0; color: #111827; font-size: 14px; font-weight: 500;'>{tableNumber}</td>
                    </tr>
                </table>
            </div>

            {requestsSection}

            <!-- Action Required Alert -->
            <div style='background: #fef3c7; border: 2px solid #fbbf24; border-radius: 12px; padding: 20px; margin: 24px 0; text-align: center;'>
                <div style='font-size: 24px; margin-bottom: 8px;'>⚠️</div>
                <strong style='color: #92400e; font-size: 16px; display: block; margin-bottom: 4px;'>{t["ActionRequired"]}</strong>
                <p style='margin: 0; color: #78350f; font-size: 14px;'>{t["ApproveOrReject"]}</p>
            </div>

            <!-- Action Buttons -->
            <div style='text-align: center; margin: 24px 0;'>
                <a href='{apiBaseUrl}/api/Reservations/{reservationId}/quick-approve' style='display: inline-block; background: linear-gradient(135deg, #10b981 0%, #059669 100%); color: white; padding: 16px 40px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px; box-shadow: 0 4px 6px rgba(16, 185, 129, 0.3); margin: 0 8px 12px 8px;'>✓ {t["ApproveButton"]}</a>
            </div>

            <div style='text-align: center; margin: 24px 0;'>
                <a href='{apiBaseUrl}/api/Reservations/{reservationId}/quick-reject' style='display: inline-block; background: #dc2626; color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 15px; box-shadow: 0 2px 4px rgba(220, 38, 38, 0.3);'>✕ {t["RejectButton"]}</a>
            </div>

            <p style='text-align: center; margin: 20px 0; padding: 16px; background: #f3f4f6; border-radius: 8px; font-size: 13px; color: #6b7280;'>
                {t.Format("Dashboard", $"<a href='{frontendUrl}/admin/reservations' style='color: #3b82f6; text-decoration: none; font-weight: 600;'>{t["DashboardLink"]}</a>")}
            </p>

            <div style='margin-top: 32px; padding-top: 24px; border-top: 1px solid #e5e7eb;'>
                <p style='margin: 0 0 8px 0; color: #6b7280; font-size: 14px;'>{t["NotifiedAutomatically"]}</p>
                <p style='margin: 0; color: #111827; font-size: 14px;'><strong>{t["BestRegards"]}</strong><br>{brand.Name}</p>
            </div>
        </div>

        <!-- Footer -->
        <div style='background: #f9fafb; padding: 24px; text-align: center; border-top: 1px solid #e5e7eb;'>
            <p style='margin: 0 0 8px 0; color: #6b7280; font-size: 13px;'><strong>{brand.Name}</strong> | {brand.City} | {email}</p>
            <p style='margin: 0; color: #9ca3af; font-size: 12px;'>{Copyright(t, brand)}</p>
        </div>
    </div>

    <!-- Dark Mode Version -->
    <div class='dark-only' style='max-width: 600px; margin: 0 auto; background: #1f2937;'>
        <!-- Header -->
        <div style='background: linear-gradient(135deg, #b8941f 0%, #d4af37 100%); padding: 32px 24px; text-align: center;'>
            <h1 style='margin: 0; color: #ffffff; font-size: 28px; font-weight: 700;'>🍽️ {brand.Name}</h1>
            <p style='margin: 8px 0 0 0; color: rgba(255,255,255,0.9); font-size: 14px;'>{t[HeadingKey]}</p>
        </div>

        <!-- Content -->
        <div style='padding: 32px 24px;'>
            <!-- Reservation ID Badge -->
            <div style='background: linear-gradient(135deg, #7c3aed 0%, #6d28d9 100%); color: white; padding: 24px; border-radius: 12px; text-align: center; margin-bottom: 24px; box-shadow: 0 4px 6px rgba(124, 58, 237, 0.3);'>
                <div style='font-size: 12px; text-transform: uppercase; letter-spacing: 1px; opacity: 0.9; margin-bottom: 4px;'>{t["ReservationIdLabel"]}</div>
                <div style='font-size: 24px; font-weight: 700; letter-spacing: 1px;'>{reservationId}</div>
            </div>

            <!-- Customer Info -->
            <div style='background: #374151; border: 1px solid #4b5563; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #f9fafb; font-size: 16px; font-weight: 600;'>👤 {t["CustomerInfoTitle"]}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px; width: 80px;'>{t["NameLabel"]}</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px; font-weight: 500;'>{EmailHtml.Encode(customerName)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px;'>{t["EmailLabel"]}</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px;'>{EmailHtml.Encode(customerEmail)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px;'>{t["PhoneLabel"]}</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px;'>{EmailHtml.Encode(customerPhone)}</td>
                    </tr>
                </table>
            </div>

            <!-- Reservation Details -->
            <div style='background: #374151; border: 1px solid #4b5563; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: #f9fafb; font-size: 16px; font-weight: 600;'>📅 {t["DetailsTitle"]}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px; width: 80px;'>{t["DateLabel"]}</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px; font-weight: 500;'>{formattedDate}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px;'>{t["TimeLabel"]}</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px; font-weight: 500;'>{formattedStartTime} - {formattedEndTime}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px;'>{t["GuestsLabel"]}</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px; font-weight: 500;'>{t.Format("GuestCount", numberOfGuests)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: #9ca3af; font-size: 14px;'>{t["TableLabel"]}</td>
                        <td style='padding: 6px 0; color: #f9fafb; font-size: 14px; font-weight: 500;'>{tableNumber}</td>
                    </tr>
                </table>
            </div>

            {requestsSection}

            <!-- Action Required Alert -->
            <div style='background: #78350f; border: 2px solid #f59e0b; border-radius: 12px; padding: 20px; margin: 24px 0; text-align: center;'>
                <div style='font-size: 24px; margin-bottom: 8px;'>⚠️</div>
                <strong style='color: #fef3c7; font-size: 16px; display: block; margin-bottom: 4px;'>{t["ActionRequired"]}</strong>
                <p style='margin: 0; color: #fde68a; font-size: 14px;'>{t["ApproveOrReject"]}</p>
            </div>

            <!-- Action Buttons -->
            <div style='text-align: center; margin: 24px 0;'>
                <a href='{apiBaseUrl}/api/Reservations/{reservationId}/quick-approve' style='display: inline-block; background: linear-gradient(135deg, #059669 0%, #047857 100%); color: white; padding: 16px 40px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px; box-shadow: 0 4px 6px rgba(5, 150, 105, 0.4); margin: 0 8px 12px 8px;'>✓ {t["ApproveButton"]}</a>
            </div>

            <div style='text-align: center; margin: 24px 0;'>
                <a href='{apiBaseUrl}/api/Reservations/{reservationId}/quick-reject' style='display: inline-block; background: #dc2626; color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 15px; box-shadow: 0 2px 4px rgba(220, 38, 38, 0.4);'>✕ {t["RejectButton"]}</a>
            </div>

            <p style='text-align: center; margin: 20px 0; padding: 16px; background: #374151; border-radius: 8px; font-size: 13px; color: #9ca3af;'>
                {t.Format("Dashboard", $"<a href='{frontendUrl}/admin/reservations' style='color: #60a5fa; text-decoration: none; font-weight: 600;'>{t["DashboardLink"]}</a>")}
            </p>

            <div style='margin-top: 32px; padding-top: 24px; border-top: 1px solid #4b5563;'>
                <p style='margin: 0 0 8px 0; color: #9ca3af; font-size: 14px;'>{t["NotifiedAutomatically"]}</p>
                <p style='margin: 0; color: #f9fafb; font-size: 14px;'><strong>{t["BestRegards"]}</strong><br>{brand.Name}</p>
            </div>
        </div>

        <!-- Footer -->
        <div style='background: #374151; padding: 24px; text-align: center; border-top: 1px solid #4b5563;'>
            <p style='margin: 0 0 8px 0; color: #9ca3af; font-size: 13px;'><strong>{brand.Name}</strong> | {brand.City} | {email}</p>
            <p style='margin: 0; color: #6b7280; font-size: 12px;'>{Copyright(t, brand)}</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, Guid reservationId, string customerName, string customerEmail, string customerPhone,
            DateTime reservationDate, TimeSpan startTime, TimeSpan endTime, int numberOfGuests, string tableNumber,
            string contactEmail,
            string? specialRequests = null)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var requestsSection = string.IsNullOrEmpty(specialRequests)
                ? ""
                : $@"

{t["SpecialRequestsLabel"]}
{specialRequests}";

            var formattedDate = reservationDate.ToString("dddd, MMMM dd, yyyy");
            var formattedStartTime = startTime.ToString(@"hh\:mm");
            var formattedEndTime = endTime.ToString(@"hh\:mm");

            return $@"{brand.Name} - {t[HeadingKey]}

📅 {t["HeadingUpper"]}

{t["ReservationIdLabel"]}: {reservationId}

{t["CustomerLabel"]} {customerName}
{t["EmailLabel"]} {customerEmail}
{t["PhoneLabel"]} {customerPhone}

{t["DetailsTitle"]}:
{t["DateLabel"]} {formattedDate}
{t["TimeLabel"]} {formattedStartTime} - {formattedEndTime}
{t["GuestsLabel"]} {t.Format("GuestCount", numberOfGuests)}
{t["TableLabel"]} {tableNumber}{requestsSection}

{t["ActionRequiredUpper"]}
{t["ApproveOrRejectText"]}

{t["LogIn"]}

{t["BestRegards"]}
{brand.Name}

{brand.Name} | {brand.City} | {email}
{Copyright(t, brand)}";
        }
    }
}
