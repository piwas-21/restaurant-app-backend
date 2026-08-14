using System.Net;

namespace RestaurantSystem.Api.Common.Templates;

/// <summary>
/// Encoding helpers for values that reach an email body from outside the application
/// (a guest's name, notes, delivery instructions).
/// <para>
/// Before this existed those values were interpolated raw into the HTML bodies, so a
/// guest could put markup — or a <c>&lt;script&gt;</c> — into the order alert the operator
/// opens in their mail client (EMAIL-LOCALISATION-PLAN §6.3). Only the values are encoded;
/// the template's own markup is not.
/// </para>
/// </summary>
public static class EmailHtml
{
    /// <summary>HTML-encodes a guest-supplied value. Null and empty pass through unchanged.</summary>
    public static string Encode(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : WebUtility.HtmlEncode(value);
}
