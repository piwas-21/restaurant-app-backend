namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// Inputs for a status page rendered by <see cref="Interfaces.IHtmlResponseBuilder"/>.
/// <see cref="BodyHtml"/> takes pre-rendered HTML — see
/// <see cref="Interfaces.IHtmlResponseBuilder.Escape"/> for handling user input.
/// </summary>
public sealed record HtmlStatusPage
{
    /// <summary>Document &lt;title&gt;.</summary>
    public required string Title { get; init; }

    /// <summary>Single emoji or unicode glyph (e.g. "✓", "✕", "⏳"). Empty for none.</summary>
    public string Icon { get; init; } = "";

    /// <summary>
    /// CSS colour for the icon and heading. Defaults to neutral grey.
    /// MUST be a hardcoded constant of the form <c>#hex3</c>, <c>#hex6</c>, or a
    /// CSS named colour (e.g. "red", "rebeccapurple"). The builder validates
    /// this with a regex before emitting; values containing other CSS syntax
    /// (e.g. <c>red; background: url(...)</c>) are rejected to prevent CSS
    /// injection in the inline <c>style="color:..."</c> attribute.
    /// HtmlEncode does NOT protect against CSS injection here — never feed
    /// user input into this property.
    /// </summary>
    public string AccentColor { get; init; } = "#374151";

    /// <summary>Visible heading (H1).</summary>
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
