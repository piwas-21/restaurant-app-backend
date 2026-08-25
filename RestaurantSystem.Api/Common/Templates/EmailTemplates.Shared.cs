using System.Globalization;

namespace RestaurantSystem.Api.Common.Templates;

/// <summary>
/// Pieces every mail repeats. They live in the shared <c>Common</c> resource set so a
/// translator writes "Best regards," once rather than fifteen times.
/// </summary>
public static partial class EmailTemplates
{
    /// <summary>Footer copyright line, e.g. "© 2026 Kebab House. All rights reserved.".</summary>
    internal static string Copyright(EmailText text, EmailBranding brand) =>
        text.Format("Copyright", DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture), brand.Name);

    /// <summary>
    /// The punctuation between a label and its value, as a language writes it: <c>":"</c> in
    /// English, <c>" :"</c> in French (a space before the colon is not optional there).
    /// </summary>
    /// <remarks>
    /// The colon used to be a literal in nine text bodies, which is why a French mail said
    /// "NUMÉRO DE COMMANDE: ORD-1" while every label INSIDE the resources spaced it correctly. It is
    /// a resource rather than a rule in code because the answer is per language and German writes it
    /// like English — a translator has to be able to decide it without touching a template.
    /// </remarks>
    internal static string Labelled(EmailText text, string label, string value) =>
        $"{label}{text["LabelColon"]} {value}";

    /// <summary>A heading that introduces the block under it — the same colon, no value.</summary>
    internal static string Heading(EmailText text, string label) => label + text["LabelColon"];

    /// <summary>
    /// The salutation line, and the reason it is a helper: an order can genuinely have no customer
    /// name (a QR or counter order the guest never typed one into), and the three order senders used
    /// to paper over that with the literal <c>"Valued Customer"</c> — which a French guest read as
    /// "Bonjour Valued Customer,". A missing name is now a missing name, and the resource set
    /// decides what to say instead.
    /// </summary>
    /// <param name="key">The set's own salutation key — some mails say "Dear", others "Hello".</param>
    /// <param name="encode">
    /// HTML bodies encode the name (it is guest-supplied, §6.3); text bodies must not.
    /// </param>
    internal static string Greeting(EmailText text, string key, string? name, bool encode = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return text["GreetingNoName"];
        }

        return text.Format(key, encode ? EmailHtml.Encode(name) : name);
    }

    /// <summary>
    /// A date a guest reads, in the language the mail is written in — "Friday, 21 August 2026" /
    /// "vendredi 21 août 2026".
    /// </summary>
    /// <remarks>
    /// The STANDARD specifier, not the <c>dddd, MMMM dd, yyyy</c> pattern these templates carried:
    /// a hand-written pattern is English WORD ORDER, so under French it produced "vendredi, août 21,
    /// 2026" — the month names translated and the sentence still American. This is the §6.2/§6.7
    /// decision the plan deferred to S7, and it is deliberately the ONLY thing here that takes the
    /// culture: amounts stay ambient (the currency is a per-tenant label, never derived from the
    /// language), which is why <c>EmailText.Format</c> still runs under the invariant culture.
    /// </remarks>
    internal static string LongDate(DateTime value, CultureInfo culture) => value.ToString("D", culture);

    /// <summary>
    /// A date and time a guest reads: the localised date, a 24-hour clock, and the offset the
    /// clock is on — "Friday, 17 May 2030 21:30 (UTC+02:00)".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The clock is deliberately NOT the culture's short-time pattern, which would render
    /// <c>7:30 PM</c> for English — every other time in the corpus is a 24-hour
    /// <c>19:30 - 21:00</c> built from a <c>TimeSpan</c>, and one mail disagreeing with the rest
    /// about how to write a clock is worse than either convention. The seconds are gone with it:
    /// "changed at 19:30:00" told a human nothing.
    /// </para>
    /// <para>
    /// The argument is a <see cref="DateTimeOffset"/> and not a <see cref="DateTime"/> BECAUSE of
    /// #363: this used to be handed <c>DateTime.UtcNow</c>, so a guest in Geneva read a UTC time
    /// as if it were the time on their own clock, one or two hours out and marked as nothing at
    /// all. The conversion belongs to <c>ITenantClock</c> at the send site; making the parameter
    /// an offset is what stops a caller silently passing a bare UTC instant again.
    /// </para>
    /// <para>
    /// The marker is a numeric offset rather than an abbreviation: .NET has no localised "CEST",
    /// and the English zone names it does have would be a third language inside a German mail.
    /// <c>zzz</c> is the offset the value carries, so it is already DST-correct for the instant.
    /// </para>
    /// </remarks>
    internal static string LongDateTime(DateTimeOffset value, CultureInfo culture) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{LongDate(value.DateTime, culture)} {value:HH:mm} (UTC{value:zzz})");

    /// <summary>
    /// Human label for an account role. The argument is a <c>UserRole</c> enum name, so without this
    /// a French welcome mail read "avec le rôle Staff." Unknown values fall through to the raw name,
    /// like <see cref="OrderTypeLabel"/> — a role the templates have not been taught is better shown
    /// in English than swallowed.
    /// </summary>
    internal static string RoleLabel(EmailText text, string role) => role switch
    {
        "Admin" => text["RoleAdmin"],
        "Staff" => text["RoleStaff"],
        "Customer" => text["RoleCustomer"],
        _ => role
    };

    /// <summary>
    /// Human label for an order type. Unknown values (the enum grew, the template did not)
    /// fall through to the raw value, exactly as before.
    /// </summary>
    internal static string OrderTypeLabel(EmailText text, string orderType, bool withEmoji = false) => orderType switch
    {
        "DineIn" => (withEmoji ? "🍽️ " : string.Empty) + text["OrderTypeDineIn"],
        "Takeaway" => (withEmoji ? "🛍️ " : string.Empty) + text["OrderTypeTakeaway"],
        "Delivery" => (withEmoji ? "🚚 " : string.Empty) + text["OrderTypeDelivery"],
        _ => orderType
    };

    /// <summary>
    /// The "who ordered / who booked" card the two operator mails both open with — byte-identical
    /// in both since the light/dark collapse (#356), and therefore extractable rather than merely
    /// similar. Indented as it appears in the document: the caller supplies only the first line's
    /// margin, because the fragment lands inside a larger verbatim string.
    /// </summary>
    internal static string AdminCustomerCard(
        EmailText t, EmailPalette p, string customerName, string customerEmail, string customerPhone) =>
        $@"<div style='background: {p.SurfaceBackground}; border: 1px solid {p.SurfaceBorder}; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
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
            </div>";

    /// <summary>
    /// The tail of an operator mail: the "open the dashboard" panel, the sign-off, and the footer
    /// line. Identical in both but for the dashboard PATH, which is the one thing the two mails
    /// disagree about (orders vs reservations) and so the one parameter.
    /// </summary>
    internal static string AdminFooter(
        EmailText t, EmailPalette p, EmailBranding brand, string email, string frontendUrl, string dashboardPath) =>
        $@"<p style='text-align: center; margin: 20px 0; padding: 16px; background: {p.FooterBackground}; border-radius: 8px; font-size: 13px; color: {p.MutedText};'>
                {t.Format("Dashboard", $"<a href='{frontendUrl}{dashboardPath}' style='color: {p.FooterLink}; text-decoration: none; font-weight: 600;'>{t["DashboardLink"]}</a>")}
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
        </div>";

    /// <summary>
    /// The document a GUEST reservation mail is posted in: the same head, the same 600px card, the
    /// same header band and the same footer, four times over (received, approved, changed,
    /// rejected). Only the accent colour, one extra CSS rule and the middle differ.
    /// </summary>
    /// <param name="accent">The card's header background and the info-box rule — one colour, two uses.</param>
    /// <param name="statusBoxCss">
    /// The one status-box rule this particular mail needs (<c>.pending</c>, <c>.confirmed</c>,
    /// <c>.notice</c>), as a CSS declaration line. A parameter rather than "ship all of them",
    /// because a mail carrying rules for boxes it never renders is dead weight in every inbox — and
    /// because keeping each mail's own rule where it is used kept all four snapshots byte-identical
    /// through this extraction.
    /// </param>
    /// <param name="content">
    /// The mail's own middle, indented as it appears inside <c>&lt;div class='content'&gt;</c>: the
    /// shell supplies the first line's margin, exactly as <see cref="AdminCustomerCard"/> does.
    /// </param>
    internal static string GuestMailDocument(
        EmailText t, EmailBranding brand, string title, string accent, string statusBoxCss, string content,
        string contactEmail) =>
        $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{title}</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: {accent}; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 30px 20px; background: #f9f9f9; }}
        .info-box {{ background: white; padding: 15px; margin: 15px 0; border-radius: 5px; border-left: 4px solid {accent}; }}
        .footer {{ padding: 20px; text-align: center; color: #666; font-size: 12px; }}
        {statusBoxCss}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🍽️ {brand.Name}</h1>
        </div>
        <div class='content'>
            {content}
        </div>
        <div class='footer'>
            <p>{brand.Name} | {brand.City} | {contactEmail}</p>
            <p>{Copyright(t, brand)}</p>
        </div>
    </div>
</body>
</html>";

    /// <summary>
    /// The envelope every operator mail is posted in: one document, the same block rendered once
    /// per colour scheme, and the two media queries that pick between them (#356).
    /// </summary>
    /// <remarks>
    /// Shared because it is byte-identical in both reservation alerts and carries no decision of
    /// its own — a second hand-written copy is 25 lines of markup that can silently drift, and a
    /// mail whose dark-mode query is a character out renders both copies at once.
    /// </remarks>
    internal static string DualSchemeDocument(string title, Func<EmailPalette, string> block)
    {
        ArgumentNullException.ThrowIfNull(block);

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <meta name='color-scheme' content='light dark'>
    <title>{title}</title>
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
    {block(EmailPalette.Light)}

    {block(EmailPalette.Dark)}
</body>
</html>";
    }

    /// <summary>The restaurant name and the mail's own subtitle, on the gradient band.</summary>
    internal static string AdminMailHeader(EmailBranding brand, EmailPalette p, string subtitle) =>
        $@"<div style='background: linear-gradient(135deg, {p.HeaderGradientFrom} 0%, {p.HeaderGradientTo} 100%); padding: 32px 24px; text-align: center;'>
            <h1 style='margin: 0; color: #ffffff; font-size: 28px; font-weight: 700;'>🍽️ {brand.Name}</h1>
            <p style='margin: 8px 0 0 0; color: rgba(255,255,255,0.9); font-size: 14px;'>{subtitle}</p>
        </div>";

    /// <summary>The reservation id, in the badge both reservation alerts open with.</summary>
    internal static string AdminReservationBadge(EmailText t, EmailPalette p, Guid reservationId) =>
        $@"<div style='background: linear-gradient(135deg, {p.ReservationBadgeGradientFrom} 0%, {p.ReservationBadgeGradientTo} 100%); color: white; padding: 24px; border-radius: 12px; text-align: center; margin-bottom: 24px; box-shadow: 0 4px 6px {p.ReservationBadgeShadow};'>
                <div style='font-size: 12px; text-transform: uppercase; letter-spacing: 1px; opacity: 0.9; margin-bottom: 4px;'>{t["ReservationIdLabel"]}</div>
                <div style='font-size: 24px; font-weight: 700; letter-spacing: 1px;'>{reservationId}</div>
            </div>";

    /// <summary>
    /// What was booked, as the restaurant reads it: day, hours, party size, table. The values
    /// arrive already formatted — the caller owns the culture, this owns the markup.
    /// </summary>
    internal static string AdminReservationDetails(
        EmailText t, EmailPalette p, string title, string date, string time, string guests, string tableNumber) =>
        $@"<div style='background: {p.SurfaceBackground}; border: 1px solid {p.SurfaceBorder}; border-radius: 12px; padding: 20px; margin-bottom: 20px;'>
                <h3 style='margin: 0 0 16px 0; color: {p.StrongText}; font-size: 16px; font-weight: 600;'>📅 {title}</h3>
                <table style='width: 100%; border-collapse: collapse;'>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px; width: 80px;'>{t["DateLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px; font-weight: 500;'>{date}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px;'>{t["TimeLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px; font-weight: 500;'>{time}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px;'>{t["GuestsLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px; font-weight: 500;'>{guests}</td>
                    </tr>
                    <tr>
                        <td style='padding: 6px 0; color: {p.MutedText}; font-size: 14px;'>{t["TableLabel"]}</td>
                        <td style='padding: 6px 0; color: {p.StrongText}; font-size: 14px; font-weight: 500;'>{EmailHtml.Encode(tableNumber)}</td>
                    </tr>
                </table>
            </div>";

    /// <summary>
    /// The amber note both operator mails print a guest's own words in — empty when there are
    /// none, so the caller interpolates it unconditionally.
    /// </summary>
    internal static string AdminGuestNote(string label, string? value) =>
        string.IsNullOrEmpty(value)
            ? ""
            : $@"<div style='background: #fef3c7; border-left: 4px solid #f59e0b; padding: 16px; margin: 20px 0; border-radius: 8px;'>
                        <strong style='color: #92400e; font-size: 14px;'>📝 {label}</strong><br>
                        <span style='color: #78350f; margin-top: 8px; display: block; white-space: pre-line;'>{EmailHtml.Encode(value)}</span>
                    </div>";

    /// <summary>
    /// The two signed quick-action URLs an operator alert links to (backend #402): the reservation
    /// route plus the per-action signature that authorises the anonymous endpoint.
    /// </summary>
    /// <remarks>
    /// Shared by both operator alerts, so the shape of the link — and the escaping of the token —
    /// is decided once. A missing token still renders a button, deliberately: it lands on "this
    /// link can no longer be used", which is the honest outcome for a mail minted without one.
    /// </remarks>
    internal static (string Approve, string Reject) ReservationQuickActionUrls(
        string apiBaseUrl, Guid reservationId, string? approveToken, string? rejectToken)
    {
        var linkBase = $"{apiBaseUrl}/api/Reservations/{reservationId}";

        return ($"{linkBase}/quick-approve?token={Uri.EscapeDataString(approveToken ?? string.Empty)}",
            $"{linkBase}/quick-reject?token={Uri.EscapeDataString(rejectToken ?? string.Empty)}");
    }

    /// <summary>
    /// The approve / reject pair an operator alert about a reservation ends with — the ONLY way to
    /// reach those endpoints from a mail, and now rendered by two mails (the new-booking alert and
    /// the changed-booking alert), which is what makes it shared rather than merely similar.
    /// </summary>
    /// <remarks>
    /// One place on purpose: backend #402 made these links SIGNED and expiring, and a second
    /// hand-written copy of the markup is exactly how one mail keeps emitting the bare form. The
    /// URLs are built by <see cref="ReservationQuickActionUrls"/>, so a template cannot assemble
    /// one without a signature even by accident. Indented as it appears in the document — the
    /// caller supplies the first line's margin.
    /// </remarks>
    internal static string ReservationQuickActions(
        EmailText t, EmailPalette p, string approveUrl, string rejectUrl) =>
        $@"<div style='text-align: center; margin: 24px 0;'>
                <a href='{approveUrl}' style='display: inline-block; background: linear-gradient(135deg, {p.ConfirmGradientFrom} 0%, {p.ConfirmGradientTo} 100%); color: white; padding: 16px 40px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px; box-shadow: 0 4px 6px {p.ConfirmButtonShadow}; margin: 0 8px 12px 8px;'>✓ {t["ApproveButton"]}</a>
            </div>

            <div style='text-align: center; margin: 24px 0;'>
                <a href='{rejectUrl}' style='display: inline-block; background: #dc2626; color: white; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 15px; box-shadow: 0 2px 4px {p.CancelButtonShadow};'>✕ {t["RejectButton"]}</a>
            </div>";

    /// <summary>
    /// The three variable pieces of an order's PLAIN-TEXT body: the item lines, and the optional
    /// instructions and delivery blocks with the blank lines that separate them.
    /// </summary>
    /// <remarks>
    /// Written out twice — once in the guest's "we got your order" and once in the operator's alert
    /// — down to the blank lines inside the verbatim strings. That is what a copy-paste detector is
    /// for, and it was invisible while `EmailTemplates.*.cs` was excluded from one (#356). The
    /// values are NOT HTML-encoded here, deliberately: this is the text body, and encoding it would
    /// print `&amp;` at a guest (§6.3 applies to the HTML side only).
    /// </remarks>
    internal static (string Items, string Instructions, string Delivery) OrderTextSections(
        EmailText t,
        IEnumerable<(string name, int quantity, decimal price)> items,
        string currency,
        string? specialInstructions,
        string? deliveryAddress)
    {
        var itemsSection = string.Join("\n", items.Select(item =>
            $"{item.name} x{item.quantity} = {currency} {item.price:F2}"));

        var instructionsSection = string.IsNullOrEmpty(specialInstructions)
            ? ""
            : $@"

{t["InstructionsLabel"]}
{specialInstructions}";

        var deliverySection = string.IsNullOrEmpty(deliveryAddress)
            ? ""
            : $@"

{t["DeliveryLabel"]}
{deliveryAddress}";

        return (itemsSection, instructionsSection, deliverySection);
    }
}
