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

        public static string GetHtmlBody(
            CultureInfo culture, EmailBranding brand, string customerName, ReservationMailDetails reservation, string contactEmail)
        {
            var (reservationDate, startTime, endTime, numberOfGuests, tableNumber, specialRequests, _, _, _) = reservation;
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var requestsSection = string.IsNullOrEmpty(specialRequests)
                ? ""
                : $@"<div class='info-box'>
                        <strong>{t["SpecialRequestsLabel"]}</strong><br>
                        {EmailHtml.Encode(specialRequests)}
                    </div>";

            var content = $@"<h2>{t["Heading"]}</h2>
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
            <p>{t["BestRegards"]}<br>{t.Format("BrandTeam", brand.Name)}</p>";

            return GuestMailDocument(
                t, brand, t["PageTitle"], "#d4af37",
                @".pending { background: #fff3cd; border: 1px solid #ffeaa7; padding: 15px; border-radius: 5px; margin: 20px 0; }", content, email);
        }

        public static string GetTextBody(
            CultureInfo culture, EmailBranding brand, string customerName, ReservationMailDetails reservation, string contactEmail)
        {
            var (reservationDate, startTime, endTime, numberOfGuests, tableNumber, specialRequests, _, _, _) = reservation;
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
