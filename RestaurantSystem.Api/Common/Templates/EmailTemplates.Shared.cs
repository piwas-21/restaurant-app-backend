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
    internal static string Greeting(EmailText text, string key, string? name, bool encode = false) =>
        string.IsNullOrWhiteSpace(name)
            ? text["GreetingNoName"]
            : text.Format(key, encode ? EmailHtml.Encode(name) : name);

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
    /// A date and time a guest reads: the localised date, then a 24-hour clock.
    /// </summary>
    /// <remarks>
    /// The clock is deliberately NOT the culture's short-time pattern, which would render
    /// <c>7:30 PM</c> for English — every other time in the corpus is a 24-hour
    /// <c>19:30 - 21:00</c> built from a <c>TimeSpan</c>, and one mail disagreeing with the rest
    /// about how to write a clock is worse than either convention. The seconds are gone with it:
    /// "changed at 19:30:00" told a human nothing. There is still no timezone marker — that half of
    /// the defect is #363.
    /// </remarks>
    internal static string LongDateTime(DateTime value, CultureInfo culture) =>
        $"{LongDate(value, culture)} {value.ToString("HH:mm", CultureInfo.InvariantCulture)}";

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
}
