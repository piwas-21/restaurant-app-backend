using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Reservation confirmation email template (sent to customer and admin)
    /// </summary>
    public static class ReservationConfirmation
    {
        private const string Set = "ReservationConfirmation";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, string customerName, string tableNumber, DateTime reservationDate,
            TimeSpan startTime, TimeSpan endTime, int numberOfGuests, string contactEmail,
            string? specialRequests = null)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var requestsSection = string.IsNullOrEmpty(specialRequests)
                ? ""
                : $@"<div class='info-box'>
                        <strong>{t["SpecialRequestsLabel"]}</strong><br>
                        {EmailHtml.Encode(specialRequests)}
                    </div>";

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{t["PageTitle"]}</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #d4af37; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .info-box {{ background: white; padding: 15px; margin: 15px 0; border-radius: 5px; border-left: 4px solid #d4af37; }}
        .footer {{ padding: 20px; text-align: center; color: #666; font-size: 12px; }}
        .pending {{ background: #fff3cd; border: 1px solid #ffeaa7; padding: 15px; border-radius: 5px; margin: 20px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍽️ {brand.Name}</h1>
        </div>
        <div class='content'>
            <h2>{t["Heading"]}</h2>
            <p>{Greeting(t, "Dear", customerName, encode: true)}</p>
            <p>{t.Format("ThankYou", brand.Name)}</p>

            <div class='info-box'>
                <strong>📅 {t["DateLabel"]}</strong> {LongDate(reservationDate, culture)}<br>
                <strong>🕐 {t["TimeLabel"]}</strong> {startTime:hh':'mm} - {endTime:hh':'mm}<br>
                <strong>👥 {t["GuestsLabel"]}</strong> {numberOfGuests}<br>
                <strong>🪑 {t["TableLabel"]}</strong> {EmailHtml.Encode(tableNumber)}
            </div>

            {requestsSection}

            <div class='pending'>
                <strong>⏳ {t["PendingTitle"]}</strong><br>
                {t["PendingBody"]}
            </div>

            <p>{t.Format("Contact", email)}</p>
            <p>{t["LookForward"]}</p>
            <p>{t["BestRegards"]}<br>{t.Format("BrandTeam", brand.Name)}</p>
        </div>
        <div class='footer'>
            <p>{brand.Name} | {brand.City} | {email}</p>
            <p>{Copyright(t, brand)}</p>
        </div>
    </div>
</body>
</html>";
        }

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, string customerName, string tableNumber, DateTime reservationDate,
            TimeSpan startTime, TimeSpan endTime, int numberOfGuests, string contactEmail,
            string? specialRequests = null)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var requestsSection = string.IsNullOrEmpty(specialRequests)
                ? ""
                : $@"

{t["SpecialRequestsLabel"]}
{specialRequests}";

            return $@"{brand.Name} - {t["Heading"]}

{Greeting(t, "Dear", customerName)}

{t.Format("ThankYou", brand.Name)}

{t["DateLabel"]} {LongDate(reservationDate, culture)}
{t["TimeLabel"]} {startTime:hh':'mm} - {endTime:hh':'mm}
{t["GuestsLabel"]} {numberOfGuests}
{t["TableLabel"]} {tableNumber}{requestsSection}

{t["PendingTitleUpper"]}
{t["PendingBody"]}

{t.Format("Contact", email)}

{t["LookForward"]}

{t["BestRegards"]}
{t.Format("BrandTeam", brand.Name)}

{brand.Name} | {brand.City} | {email}
{Copyright(t, brand)}";
        }
    }
}
