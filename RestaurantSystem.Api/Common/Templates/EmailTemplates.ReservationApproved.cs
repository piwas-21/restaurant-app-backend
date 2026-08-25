using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// Reservation approved email template (sent to customer)
    /// </summary>
    public static class ReservationApproved
    {
        private const string Set = "ReservationApproved";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(
            CultureInfo culture, EmailBranding brand, string customerName, ReservationMailDetails reservation,
            string contactEmail, string? notes = null)
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

            var notesSection = string.IsNullOrEmpty(notes)
                ? ""
                : $@"<div class='info-box' style='border-left-color: #27ae60;'>
                        <strong>{t["NoteLabel"]}</strong><br>
                        {EmailHtml.Encode(notes)}
                    </div>";

            var content = $@"<div class='confirmed'>
                <h2 style='margin: 0; color: #27ae60;'>✅ {t["Confirmed"]}</h2>
            </div>

            <p>{Greeting(t, "Dear", customerName, encode: true)}</p>
            <p>{t.Format("GoodNews", brand.Name)}</p>

            <div class='info-box'>
                <strong>📅 {t["DateLabel"]}</strong> {LongDate(reservationDate, culture)}<br>
                <strong>🕐 {t["TimeLabel"]}</strong> {startTime:hh':'mm} - {endTime:hh':'mm}<br>
                <strong>👥 {t["GuestsLabel"]}</strong> {numberOfGuests}<br>
                <strong>🪑 {t["TableLabel"]}</strong> {EmailHtml.Encode(tableNumber)}
            </div>

            {requestsSection}
            {notesSection}

            <p><strong>{t["ImportantInfoLabel"]}</strong></p>
            <ul>
                <li>{t["Info1"]}</li>
                <li>{t["Info2"]}</li>
                <li>{t.Format("Info3", email)}</li>
            </ul>

            <p>{t["LookForwardWelcoming"]}</p>
            <p>{t["BestRegards"]}<br>{t.Format("BrandTeam", brand.Name)}</p>";

            return GuestMailDocument(
                t, brand, t["Heading"], "#27ae60",
                @".confirmed { background: #d4edda; border: 1px solid #c3e6cb; padding: 15px; border-radius: 5px; margin: 20px 0; text-align: center; }", content, email);
        }

        public static string GetTextBody(
            CultureInfo culture, EmailBranding brand, string customerName, ReservationMailDetails reservation,
            string contactEmail, string? notes = null)
        {
            var (reservationDate, startTime, endTime, numberOfGuests, tableNumber, specialRequests, _, _, _) = reservation;
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var requestsSection = string.IsNullOrEmpty(specialRequests)
                ? ""
                : $@"

{t["SpecialRequestsLabel"]}
{specialRequests}";

            var notesSection = string.IsNullOrEmpty(notes)
                ? ""
                : $@"

{t["NoteLabel"]}
{notes}";

            return $@"{brand.Name} - {t["Heading"]}

✅ {t["ConfirmedUpper"]}

{Greeting(t, "Dear", customerName)}

{t.Format("GoodNews", brand.Name)}

{t["DateLabel"]} {LongDate(reservationDate, culture)}
{t["TimeLabel"]} {startTime:hh':'mm} - {endTime:hh':'mm}
{t["GuestsLabel"]} {numberOfGuests}
{t["TableLabel"]} {tableNumber}{requestsSection}{notesSection}

{t["ImportantInfoLabel"]}
- {t["Info1"]}
- {t["Info2"]}
- {t.Format("Info3", email)}

{t["LookForwardWelcoming"]}

{t["BestRegards"]}
{t.Format("BrandTeam", brand.Name)}

{brand.Name} | {brand.City} | {email}
{Copyright(t, brand)}";
        }
    }
}
