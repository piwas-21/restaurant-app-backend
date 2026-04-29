namespace RestaurantSystem.Api.Common.Services.Interfaces;

/// <summary>
/// Renders HTML status pages used by email-link landing endpoints
/// (quick-confirm/cancel order, approve/reject delay, approve/reject
/// reservation). Replaces inline `Content($@"<html>...</html>", "text/html")`
/// blocks scattered across controllers — see Sprint 2 task 2.1.
/// </summary>
public interface IHtmlResponseBuilder
{
    /// <summary>
    /// Build a complete HTML5 document for a status / result page.
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

/// <summary>
/// Inputs for a status page. <see cref="BodyHtml"/> takes pre-rendered
/// HTML — see <see cref="IHtmlResponseBuilder.Escape"/> for user input.
/// </summary>
public sealed record HtmlStatusPage
{
    /// <summary>Document &lt;title&gt;.</summary>
    public required string Title { get; init; }

    /// <summary>Single emoji or unicode glyph (e.g. "✓", "✕", "⏳"). Empty for none.</summary>
    public string Icon { get; init; } = "";

    /// <summary>CSS colour for the icon and heading. Defaults to neutral grey.</summary>
    public string AccentColor { get; init; } = "#374151";

    /// <summary>Visible heading (H1 in Card style, H2 in Plain).</summary>
    public required string Heading { get; init; }

    /// <summary>Pre-rendered body HTML. Caller is responsible for escaping user input.</summary>
    public required string BodyHtml { get; init; }

    /// <summary>Visual style. Defaults to Card (boxed, shadowed, modern).</summary>
    public HtmlPageStyle Style { get; init; } = HtmlPageStyle.Card;

    /// <summary>Optional auto-redirect via &lt;meta http-equiv="refresh"&gt;.</summary>
    public HtmlRedirect? Redirect { get; init; }

    /// <summary>Append a "Close this window" link — matches the reservation-page pattern.</summary>
    public bool ShowCloseLink { get; init; }
}

/// <summary>Visual styles for status pages.</summary>
public enum HtmlPageStyle
{
    /// <summary>Legacy plain centered text — used by OrdersController quick-confirm/cancel.</summary>
    Plain,

    /// <summary>Boxed white card with shadow — used by ApproveDelay/RejectDelay and all reservation pages.</summary>
    Card,
}

/// <summary>Auto-redirect descriptor for &lt;meta http-equiv="refresh"&gt;.</summary>
public sealed record HtmlRedirect(string Url, int DelaySeconds);
