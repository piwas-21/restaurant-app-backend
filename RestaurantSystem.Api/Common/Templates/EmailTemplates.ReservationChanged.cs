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
        .confirmed {{ background: #d4edda; border: 1px solid #c3e6cb; padding: 15px; border-radius: 5px; margin: 20px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍽️ {brand.Name}</h1>
        </div>
        <div class='content'>
            <h2>✏️ {t["Heading"]}</h2>
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
