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
