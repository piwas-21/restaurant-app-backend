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

            var formattedDate = LongDate(reservationDate, culture);
            var formattedStartTime = startTime.ToString(@"hh\:mm");
            var formattedEndTime = endTime.ToString(@"hh\:mm");

            // ONE block, rendered twice: the light and dark copies differed by nothing but colour,
            // which is the duplication `sonar.cpd.exclusions` was hiding (#356). A local function
            // rather than a method: it reads the locals above.
            string Block(EmailPalette p) => $@"<!-- {p.ModeName} Mode Version -->
    <div class='{p.ModeClass}' style='max-width: 600px; margin: 0 auto; background: {p.PageBackground};'>
        <!-- Header -->
        <div style='background: linear-gradient(135deg, {p.HeaderGradientFrom} 0%, {p.HeaderGradientTo} 100%); padding: 32px 24px; text-align: center;'>
            <h1 style='margin: 0; color: #ffffff; font-size: 28px; font-weight: 700;'>🍽️ {brand.Name}</h1>
            <p style='margin: 8px 0 0 0; color: rgba(255,255,255,0.9); font-size: 14px;'>{t[HeadingKey]}</p>
        </div>

        <!-- Content -->
        <div style='padding: 32px 24px;'>
            <!-- Reservation ID Badge -->
            <div style='background: linear-gradient(135deg, {p.ReservationBadgeGradientFrom} 0%, {p.ReservationBadgeGradientTo} 100%); color: white; padding: 24px; border-radius: 12px; text-align: center; margin-bottom: 24px; box-shadow: 0 4px 6px {p.ReservationBadgeShadow};'>
                <div style='font-size: 12px; text-transform: uppercase; letter-spacing: 1px; opacity: 0.9; margin-bottom: 4px;'>{t["ReservationIdLabel"]}</div>
                <div style='font-size: 24px; font-weight: 700; letter-spacing: 1px;'>{reservationId}</div>
            </div>

            <!-- Customer Info -->
            <div style='background: {p.SurfaceBackground}; border: 1px solid {p.SurfaceBorder}; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: {p.StrongText}; font-size: 16px; font-weight: 600;'>👤 {t["CustomerInfoTitle"]}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px; width: 80px;'>{t["NameLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px; font-weight: 500;'>{EmailHtml.Encode(customerName)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px;'>{t["EmailLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px;'>{EmailHtml.Encode(customerEmail)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px;'>{t["PhoneLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px;'>{EmailHtml.Encode(customerPhone)}</td>
                    </tr>
                </table>
            </div>

            <!-- Reservation Details -->
            <div style='background: {p.SurfaceBackground}; border: 1px solid {p.SurfaceBorder}; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: {p.StrongText}; font-size: 16px; font-weight: 600;'>📅 {t["DetailsTitle"]}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px; width: 80px;'>{t["DateLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px; font-weight: 500;'>{formattedDate}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px;'>{t["TimeLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px; font-weight: 500;'>{formattedStartTime} - {formattedEndTime}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px;'>{t["GuestsLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px; font-weight: 500;'>{t.Format("GuestCount", numberOfGuests)}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px;'>{t["TableLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px; font-weight: 500;'>{EmailHtml.Encode(tableNumber)}</td>
                    </tr>
                </table>
            </div>

            {requestsSection}

            <!-- Action Required Alert -->
            <div style='background: {p.NoticeBackground}; border: 2px solid {p.NoticeBorder}; border-radius: 12px; padding: 20px; margin: 24px 0; text-align: center;'>
                <div style='font-size: 24px; margin-bottom: 8px;'>⚠️</div>
                <strong style='color: {p.NoticeHeading}; font-size: 16px; display: block; margin-bottom: 4px;'>{t["ActionRequired"]}</strong>
                <p style='margin: 0; color: {p.NoticeText}; font-size: 14px;'>{t["ApproveOrReject"]}</p>
            </div>

            <!-- Action Buttons -->
            <div style='text-align: center; margin: 24px 0;'>
                <a href='{apiBaseUrl}/api/Reservations/{reservationId}/quick-approve' style='display: inline-block; background: linear-gradient(135deg, {p.ConfirmGradientFrom} 0%, {p.ConfirmGradientTo} 100%); color: white; padding: 16px 40px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px; box-shadow: 0 4px 6px {p.ConfirmButtonShadow}; margin: 0 8px 12px 8px;'>✓ {t["ApproveButton"]}</a>
            </div>

            <div style='text-align: center; margin: 24px 0;'>
                <a href='{apiBaseUrl}/api/Reservations/{reservationId}/quick-reject' style='display: inline-block; background: #dc2626; color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 15px; box-shadow: 0 2px 4px {p.CancelButtonShadow};'>✕ {t["RejectButton"]}</a>
            </div>

            <p style='text-align: center; margin: 20px 0; padding: 16px; background: {p.FooterBackground}; border-radius: 8px; font-size: 13px; color: {p.MutedText};'>
                {t.Format("Dashboard", $"<a href='{frontendUrl}/admin/reservations' style='color: {p.FooterLink}; text-decoration: none; font-weight: 600;'>{t["DashboardLink"]}</a>")}
            </p>

            <div style='margin-top: 32px; padding-top: 24px; border-top: 1px solid {p.SurfaceBorder};'>
                <p style='margin: 0 0 8px 0; color: {p.MutedText}; font-size: 14px;'>{t["NotifiedAutomatically"]}</p>
                <p style='margin: 0; color: {p.StrongText}; font-size: 14px;'><strong>{t["BestRegards"]}</strong><br>{brand.Name}</p>
            </div>
        </div>

        <!-- Footer -->
        <div style='background: {p.SurfaceBackground}; padding: 24px; text-align: center; border-top: 1px solid {p.SurfaceBorder};'>
            <p style='margin: 0 0 8px 0; color: {p.MutedText}; font-size: 13px;'><strong>{brand.Name}</strong> | {brand.City} | {email}</p>
            <p style='margin: 0; color: {p.FooterText}; font-size: 12px;'>{Copyright(t, brand)}</p>
        </div>
    </div>";

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
    {Block(EmailPalette.Light)}

    {Block(EmailPalette.Dark)}
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

            var formattedDate = LongDate(reservationDate, culture);
            var formattedStartTime = startTime.ToString(@"hh\:mm");
            var formattedEndTime = endTime.ToString(@"hh\:mm");

            return $@"{brand.Name} - {t[HeadingKey]}

📅 {t["HeadingUpper"]}

{Labelled(t, t["ReservationIdLabel"], reservationId.ToString())}

{t["CustomerLabel"]} {customerName}
{t["EmailLabel"]} {customerEmail}
{t["PhoneLabel"]} {customerPhone}

{Heading(t, t["DetailsTitle"])}
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
