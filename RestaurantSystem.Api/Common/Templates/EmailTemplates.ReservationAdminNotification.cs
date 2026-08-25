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

        public static string GetHtmlBody(
            CultureInfo culture, EmailBranding brand, EmailGuest guest, ReservationMailDetails reservation, EmailLinks links)
        {
            var t = EmailText.For(culture, Set);
            var (customerName, customerEmail, customerPhone) = guest;
            var (reservationDate, startTime, endTime, numberOfGuests, tableNumber, specialRequests,
                reservationId, approveToken, rejectToken) = reservation;
            var (apiBaseUrl, frontendUrl, email) = links;

            // Built once here rather than twice inline: the block below is rendered for both colour
            // schemes. The token is what authorises the anonymous endpoint (backend #402) — before
            // it, the reservation id alone was the whole authorisation, and POST /api/Reservations
            // hands that id to the guest who made the booking.
            var (approveUrl, rejectUrl) =
                ReservationQuickActionUrls(apiBaseUrl, reservationId, approveToken, rejectToken);

            var requestsSection = AdminGuestNote(t["SpecialRequestsLabel"], specialRequests);

            var formattedDate = LongDate(reservationDate, culture);
            var formattedStartTime = startTime.ToString(@"hh\:mm");
            var formattedEndTime = endTime.ToString(@"hh\:mm");

            // ONE block, rendered twice: the light and dark copies differed by nothing but colour,
            // which is the duplication `sonar.cpd.exclusions` was hiding (#356). A local function
            // rather than a method: it reads the locals above.
            string Block(EmailPalette p) => $@"<!-- {p.ModeName} Mode Version -->
    <div class='{p.ModeClass}' style='max-width: 600px; margin: 0 auto; background: {p.PageBackground};'>
        <!-- Header -->
        {AdminMailHeader(brand, p, t[HeadingKey])}

        <!-- Content -->
        <div style='padding: 32px 24px;'>
            <!-- Reservation ID Badge -->
            {AdminReservationBadge(t, p, reservationId)}

            <!-- Customer Info -->
            {AdminCustomerCard(t, p, customerName, customerEmail, customerPhone)}

            <!-- Reservation Details -->
            {AdminReservationDetails(t, p, t["DetailsTitle"], formattedDate, $"{formattedStartTime} - {formattedEndTime}", t.Format("GuestCount", numberOfGuests), tableNumber)}

            {requestsSection}

            <!-- Action Required Alert -->
            <div style='background: {p.NoticeBackground}; border: 2px solid {p.NoticeBorder}; border-radius: 12px; padding: 20px; margin: 24px 0; text-align: center;'>
                <div style='font-size: 24px; margin-bottom: 8px;'>⚠️</div>
                <strong style='color: {p.NoticeHeading}; font-size: 16px; display: block; margin-bottom: 4px;'>{t["ActionRequired"]}</strong>
                <p style='margin: 0; color: {p.NoticeText}; font-size: 14px;'>{t["ApproveOrReject"]}</p>
            </div>

            <!-- Action Buttons -->
            {ReservationQuickActions(t, p, approveUrl, rejectUrl)}

            {AdminFooter(t, p, brand, email, frontendUrl, "/admin/reservations")}
    </div>";

            return DualSchemeDocument(t[HeadingKey], Block);
        }

        public static string GetTextBody(
            CultureInfo culture, EmailBranding brand, EmailGuest guest, ReservationMailDetails reservation, string contactEmail)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var (customerName, customerEmail, customerPhone) = guest;
            var (reservationDate, startTime, endTime, numberOfGuests, tableNumber, specialRequests, reservationId, _, _) = reservation;
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
