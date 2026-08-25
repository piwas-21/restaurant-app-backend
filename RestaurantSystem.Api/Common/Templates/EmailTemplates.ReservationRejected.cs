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

            var content = $@"<h2>{t["Heading"]}</h2>
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
            <p>{t["BestRegards"]}<br>{t.Format("BrandTeam", brand.Name)}</p>";

            return GuestMailDocument(
                t, brand, t["Heading"], "#d4af37",
                @".notice { background: #fee; border: 1px solid #fcc; padding: 15px; border-radius: 5px; margin: 20px 0; }", content, email);
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
