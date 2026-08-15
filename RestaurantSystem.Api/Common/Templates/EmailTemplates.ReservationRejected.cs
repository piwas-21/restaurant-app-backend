using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Reservation rejection email template (sent to customer)
    /// </summary>
    public static class ReservationRejected
    {
        private const string Set = "ReservationRejected";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(CultureInfo culture, EmailBranding brand, string customerName, DateTime reservationDate, TimeSpan startTime, int numberOfGuests, string contactEmail)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var formattedDate = LongDate(reservationDate, culture);

            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{t["Heading"]}</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #d4af37; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .info-box {{ background: white; padding: 15px; margin: 15px 0; border-radius: 5px; border-left: 4px solid #d4af37; }}
        .footer {{ padding: 20px; text-align: center; color: #666; font-size: 12px; }}
        .notice {{ background: #fee; border: 1px solid #fcc; padding: 15px; border-radius: 5px; margin: 20px 0; }}
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
            <p>{t["Regret"]}</p>

            <div class='info-box'>
                <strong>📅 {t["DateLabel"]}</strong> {formattedDate}<br>
                <strong>🕐 {t["TimeLabel"]}</strong> {startTime:hh\:mm}<br>
                <strong>👥 {t["GuestsLabel"]}</strong> {numberOfGuests}
            </div>

            <div class='notice'>
                <strong>❌ {t["Apology"]}</strong><br>
                {t["CannotConfirm"]}
            </div>

            <p>{t["TryAnother"]}</p>
            <p>{t.Format("Questions", email)}</p>
            <p>{t["HopeToWelcome"]}</p>
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

        public static string GetTextBody(CultureInfo culture, EmailBranding brand, string customerName, DateTime reservationDate, TimeSpan startTime, int numberOfGuests, string contactEmail)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var formattedDate = LongDate(reservationDate, culture);

            return $@"{brand.Name} - {t["Heading"]}

{Greeting(t, "Dear", customerName)}

{t["Regret"]}

{t["RequestedLabel"]}
{t["DateLabel"]} {formattedDate}
{t["TimeLabel"]} {startTime:hh\:mm}
{t["GuestsLabel"]} {numberOfGuests}

{t["CannotConfirmUpper"]}
{t["CannotConfirm"]}

{t["TryAnother"]}

{t.Format("Questions", email)}

{t["HopeToWelcome"]}

{t["BestRegards"]}
{t.Format("BrandTeam", brand.Name)}

{brand.Name} | {brand.City} | {email}
{Copyright(t, brand)}";
        }
    }
}
