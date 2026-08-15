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
