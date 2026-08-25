using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// M16 — "your reservation was updated" (sent to the customer after they edit their own
    /// booking through <c>PUT /api/Reservations/{id}/mine</c>).
    /// </summary>
    /// <remarks>
    /// The booking is printed as it NOW stands, not as a diff: the guest just typed the new values
    /// and the useful thing to hold is the booking itself. What the diff decided is the status
    /// block — see <see cref="ReservationChangeOutcome"/>.
    /// </remarks>
    public static class ReservationChanged
    {
        private const string Set = "ReservationChanged";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(
            CultureInfo culture, EmailBranding brand, string customerName, ReservationMailDetails reservation,
            ReservationChangeOutcome outcome, string contactEmail)
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

            var (statusClass, statusIcon, statusTitle, statusBody) = Status(t, brand, outcome);

            var content = $@"<h2>✏️ {t["Heading"]}</h2>
            <p>{Greeting(t, "Dear", customerName, encode: true)}</p>
            <p>{t.Format("Intro", brand.Name)}</p>

            <div class='info-box'>
                <strong>📅 {t["DateLabel"]}</strong> {LongDate(reservationDate, culture)}<br>
                <strong>🕐 {t["TimeLabel"]}</strong> {startTime:hh':'mm} - {endTime:hh':'mm}<br>
                <strong>👥 {t["GuestsLabel"]}</strong> {numberOfGuests}<br>
                <strong>🪑 {t["TableLabel"]}</strong> {EmailHtml.Encode(tableNumber)}
            </div>

            {requestsSection}

            <div class='{statusClass}'>
                <strong>{statusIcon} {statusTitle}</strong><br>
                {statusBody}
            </div>

            <p>{t.Format("NotYou", email)}</p>
            <p>{t["LookForward"]}</p>
            <p>{t["BestRegards"]}<br>{t.Format("BrandTeam", brand.Name)}</p>";

            // Two status boxes because this mail can end on either — the pending amber or
            // the confirmed green — and which one is a run-time decision.
            return GuestMailDocument(
                t, brand, t["PageTitle"], "#d4af37",
                @".pending { background: #fff3cd; border: 1px solid #ffeaa7; padding: 15px; border-radius: 5px; margin: 20px 0; }
        .confirmed { background: #d4edda; border: 1px solid #c3e6cb; padding: 15px; border-radius: 5px; margin: 20px 0; }",
                content, email);
        }

        public static string GetTextBody(
            CultureInfo culture, EmailBranding brand, string customerName, ReservationMailDetails reservation,
            ReservationChangeOutcome outcome, string contactEmail)
        {
            var (reservationDate, startTime, endTime, numberOfGuests, tableNumber, specialRequests, _, _, _) = reservation;
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var requestsSection = string.IsNullOrEmpty(specialRequests)
                ? ""
                : $@"

{t["SpecialRequestsLabel"]}
{specialRequests}";

            var (_, _, _, statusBody) = Status(t, brand, outcome);

            return $@"{brand.Name} - {t["Heading"]}

{Greeting(t, "Dear", customerName)}

{t.Format("Intro", brand.Name)}

{t["DateLabel"]} {LongDate(reservationDate, culture)}
{t["TimeLabel"]} {startTime:hh':'mm} - {endTime:hh':'mm}
{t["GuestsLabel"]} {numberOfGuests}
{t["TableLabel"]} {tableNumber}{requestsSection}

{StatusTitleUpper(t, outcome)}
{statusBody}

{t.Format("NotYou", email)}

{t["LookForward"]}

{t["BestRegards"]}
{t.Format("BrandTeam", brand.Name)}

{brand.Name} | {brand.City} | {email}
{Copyright(t, brand)}";
        }

        /// <summary>
        /// The one block that differs between the three outcomes. The restaurant's name is a
        /// format argument rather than prose because "the restaurant" is not what a guest calls it.
        /// </summary>
        private static (string Class, string Icon, string Title, string Body) Status(
            EmailText t, EmailBranding brand, ReservationChangeOutcome outcome) => outcome switch
            {
                ReservationChangeOutcome.NeedsApprovalAgain =>
                    ("pending", "⏳", t["ReapprovalTitle"], t.Format("ReapprovalBody", brand.Name)),
                ReservationChangeOutcome.AwaitingApproval =>
                    ("pending", "⏳", t["PendingTitle"], t.Format("PendingBody", brand.Name)),
                _ => ("confirmed", "✅", t["ConfirmedTitle"], t["ConfirmedBody"])
            };

        private static string StatusTitleUpper(EmailText t, ReservationChangeOutcome outcome) => outcome switch
        {
            ReservationChangeOutcome.NeedsApprovalAgain => t["ReapprovalTitleUpper"],
            ReservationChangeOutcome.AwaitingApproval => t["PendingTitleUpper"],
            _ => t["ConfirmedTitleUpper"]
        };
    }
}
