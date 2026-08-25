using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

public static partial class EmailTemplates
{
    /// <summary>
    /// M17 — the restaurant's alert that a guest changed a booking whose shape it had already been
    /// asked about, carrying the same approve / reject links as the new-booking alert (M15).
    /// </summary>
    /// <remarks>
    /// Sent ONLY when the day, time or party size moved. A contact-detail fix sends the restaurant
    /// nothing: it changes no decision, and an alert mail that arrives for a corrected phone number
    /// is how the alert that matters stops being read (backend #407).
    /// </remarks>
    public static class ReservationChangedAdmin
    {
        private const string Set = "ReservationChangedAdmin";

        /// <summary>The mail's own title, rendered in the head, in both colour schemes and in the text body.</summary>
        private const string HeadingKey = "Heading";

        /// <summary>24-hour wall clock — four sittings are printed, the new one and the old one, twice.</summary>
        private const string ClockFormat = @"hh\:mm";

        /// <summary>"{0} people" — the party size, printed for both the new booking and the previous one.</summary>
        private const string GuestCountKey = "GuestCount";

        public static string GetSubject(CultureInfo culture, EmailBranding brand) =>
            EmailText.For(culture, Set).Format("Subject", brand.Name);

        public static string GetHtmlBody(
            CultureInfo culture, EmailBranding brand, EmailGuest guest, ReservationMailDetails reservation,
            ReservationPreviousBooking previous, EmailLinks links)
        {
            var t = EmailText.For(culture, Set);
            var (customerName, customerEmail, customerPhone) = guest;
            var (reservationDate, startTime, endTime, numberOfGuests, tableNumber, specialRequests,
                reservationId, approveToken, rejectToken) = reservation;
            var (apiBaseUrl, frontendUrl, email) = links;

            // Signed, and minted for the status the booking is in now (backend #402). The buttons in
            // the mail this one supersedes were signed over the OLD status, so they are already dead
            // — which is what makes "the earlier email is out of date" true and not merely polite.
            var (approveUrl, rejectUrl) =
                ReservationQuickActionUrls(apiBaseUrl, reservationId, approveToken, rejectToken);

            var requestsSection = AdminGuestNote(t["SpecialRequestsLabel"], specialRequests);

            var formattedDate = LongDate(reservationDate, culture);
            var formattedStartTime = startTime.ToString(ClockFormat);
            var formattedEndTime = endTime.ToString(ClockFormat);
            var previousDate = LongDate(previous.Date, culture);
            var previousTime =
                $"{previous.StartTime.ToString(ClockFormat)} - {previous.EndTime.ToString(ClockFormat)}";
            var lead = previous.WasConfirmed ? t["WasConfirmed"] : t["WasPending"];

            // One block, rendered twice — the light and dark copies differ by nothing but colour
            // (#356). A local function rather than a method: it reads the locals above.
            string Block(EmailPalette p) => $@"<!-- {p.ModeName} Mode Version -->
    <div class='{p.ModeClass}' style='max-width: 600px; margin: 0 auto; background: {p.PageBackground};'>
        <!-- Header -->
        {AdminMailHeader(brand, p, t[HeadingKey])}

        <!-- Content -->
        <div style='padding: 32px 24px;'>
            <!-- Reservation ID Badge -->
            {AdminReservationBadge(t, p, reservationId)}

            <p style='margin: 0 0 20px 0; color: {p.StrongText}; font-size: 15px;'>{lead}</p>

            <!-- Customer Info -->
            {AdminCustomerCard(t, p, customerName, customerEmail, customerPhone)}

            <!-- Reservation Details -->
            {AdminReservationDetails(t, p, t["DetailsTitle"], formattedDate, $"{formattedStartTime} - {formattedEndTime}", t.Format(GuestCountKey, numberOfGuests), tableNumber)}

            <!-- What it used to be -->
            <div style='background: {p.FooterBackground}; border: 1px dashed {p.SurfaceBorder}; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 12px 0; color: {p.MutedText}; font-size: 14px; font-weight: 600;'>🕘 {t["PreviousTitle"]}</h3>
                <p style='margin: 0; color: {p.MutedText}; font-size: 14px; text-decoration: line-through;'>{previousDate} · {previousTime} · {t.Format(GuestCountKey, previous.NumberOfGuests)}</p>
            </div>

            {requestsSection}

            <!-- Action Required Alert -->
            <div style='background: {p.NoticeBackground}; border: 2px solid {p.NoticeBorder}; border-radius: 12px; padding: 20px; margin: 24px 0; text-align: center;'>
                <div style='font-size: 24px; margin-bottom: 8px;'>⚠️</div>
                <strong style='color: {p.NoticeHeading}; font-size: 16px; display: block; margin-bottom: 4px;'>{t["ActionRequired"]}</strong>
                <p style='margin: 0; color: {p.NoticeText}; font-size: 14px;'>{t["ApproveOrReject"]}</p>
                <p style='margin: 8px 0 0 0; color: {p.NoticeText}; font-size: 13px;'>{t["EarlierEmail"]}</p>
            </div>

            <!-- Action Buttons -->
            {ReservationQuickActions(t, p, approveUrl, rejectUrl)}

            {AdminFooter(t, p, brand, email, frontendUrl, "/admin/reservations")}
    </div>";

            return DualSchemeDocument(t[HeadingKey], Block);
        }

        public static string GetTextBody(
            CultureInfo culture, EmailBranding brand, EmailGuest guest, ReservationMailDetails reservation,
            ReservationPreviousBooking previous, string contactEmail)
        {
            var t = EmailText.For(culture, Set);
            var email = contactEmail;
            var (customerName, customerEmail, customerPhone) = guest;
            var (reservationDate, startTime, endTime, numberOfGuests, tableNumber, specialRequests,
                reservationId, _, _) = reservation;
            var requestsSection = string.IsNullOrEmpty(specialRequests)
                ? ""
                : $@"

{t["SpecialRequestsLabel"]}
{specialRequests}";

            var formattedStartTime = startTime.ToString(ClockFormat);
            var formattedEndTime = endTime.ToString(ClockFormat);
            var previousTime =
                $"{previous.StartTime.ToString(ClockFormat)} - {previous.EndTime.ToString(ClockFormat)}";

            return $@"{brand.Name} - {t[HeadingKey]}

📅 {t["HeadingUpper"]}

{Labelled(t, t["ReservationIdLabel"], reservationId.ToString())}

{(previous.WasConfirmed ? t["WasConfirmed"] : t["WasPending"])}

{t["CustomerLabel"]} {customerName}
{t["EmailLabel"]} {customerEmail}
{t["PhoneLabel"]} {customerPhone}

{Heading(t, t["DetailsTitle"])}
{t["DateLabel"]} {LongDate(reservationDate, culture)}
{t["TimeLabel"]} {formattedStartTime} - {formattedEndTime}
{t["GuestsLabel"]} {t.Format(GuestCountKey, numberOfGuests)}
{t["TableLabel"]} {tableNumber}{requestsSection}

{Heading(t, t["PreviousTitle"])}
{t["DateLabel"]} {LongDate(previous.Date, culture)}
{t["TimeLabel"]} {previousTime}
{t["GuestsLabel"]} {t.Format(GuestCountKey, previous.NumberOfGuests)}

{t["ActionRequiredUpper"]}
{t["ApproveOrRejectText"]}
{t["EarlierEmail"]}

{t["LogIn"]}

{t["BestRegards"]}
{brand.Name}

{brand.Name} | {brand.City} | {email}
{Copyright(t, brand)}";
        }
    }
}
