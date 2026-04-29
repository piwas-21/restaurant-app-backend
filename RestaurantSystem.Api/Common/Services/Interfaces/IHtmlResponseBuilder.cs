using RestaurantSystem.Api.Common.Services;

namespace RestaurantSystem.Api.Common.Services.Interfaces;

/// <summary>
/// Renders HTML status pages used by email-link landing endpoints
/// (quick-confirm/cancel order, approve/reject delay, approve/reject
/// reservation). Replaces inline `Content($@"<html>...</html>", "text/html")`
/// blocks scattered across controllers — see Sprint 2 task 2.1.
///
/// Inputs are described by <see cref="HtmlStatusPage"/> in the
/// <c>RestaurantSystem.Api.Common.Services</c> namespace.
/// </summary>
public interface IHtmlResponseBuilder
{
    /// <summary>
    /// Build a complete HTML5 document for a status / result page.
    /// Throws <see cref="ArgumentException"/> if <see cref="HtmlStatusPage.AccentColor"/>
    /// is not a recognised CSS color literal — see that property's documentation.
    /// </summary>
    string BuildStatusPage(HtmlStatusPage page);

    /// <summary>
    /// HTML-escape a user-supplied or domain string for safe inclusion
    /// in <see cref="HtmlStatusPage.BodyHtml"/>. Callers MUST escape any
    /// value that originates from user input (URL path/query, request
    /// body, DB-stored free text). Trusted markup (e.g. <c>&lt;strong&gt;</c>
    /// wrappers) should compose escaped values, not raw input.
    /// </summary>
    string Escape(string? value);
}
