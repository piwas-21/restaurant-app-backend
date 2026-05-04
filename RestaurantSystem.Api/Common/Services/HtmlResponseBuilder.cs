using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using RestaurantSystem.Api.Common.Services.Interfaces;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// Default <see cref="IHtmlResponseBuilder"/> implementation. Pure string
/// composition — no I/O, no DI dependencies. Registered as singleton.
/// </summary>
public sealed class HtmlResponseBuilder : IHtmlResponseBuilder
{
    // Hex (#abc / #abcdef) or CSS named colour (a-z letters, allowing
    // composite names like "rebeccapurple"). Rejects anything that could
    // close the style attribute or open another CSS declaration — defence
    // in depth even though AccentColor is documented as never-user-input.
    private static readonly Regex CssColorPattern = new(
        @"^(#[0-9a-fA-F]{3}|#[0-9a-fA-F]{6}|[a-zA-Z]+)$",
        RegexOptions.Compiled);

    public string Escape(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);

    public string BuildStatusPage(HtmlStatusPage page)
    {
        if (!CssColorPattern.IsMatch(page.AccentColor))
        {
            throw new ArgumentException(
                $"AccentColor must be a hex (#abc / #abcdef) or named CSS colour. Got: '{page.AccentColor}'.",
                nameof(page));
        }

        var title = Escape(page.Title);
        var heading = Escape(page.Heading);
        var icon = Escape(page.Icon);
        // AccentColor is regex-validated above; Escape() would alter named
        // colours unnecessarily (no HTML chars present anyway).
        var color = page.AccentColor;
        // BodyHtml is intentionally not escaped — it's pre-rendered HTML
        // (see IHtmlResponseBuilder.Escape for the contract).
        var body = page.BodyHtml;

        var sb = new StringBuilder(1024);
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='en'>");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset='utf-8'>");
        sb.AppendLine("    <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.Append("    <title>").Append(title).AppendLine("</title>");

        if (page.Redirect is { } redirect)
        {
            // Escape the URL for HTML-attribute correctness: a query string
            // like ?a=1&b=2 must encode the ampersand.
            sb.Append("    <meta http-equiv='refresh' content='")
              .Append(redirect.DelaySeconds)
              .Append(";url=")
              .Append(Escape(redirect.Url))
              .AppendLine("'>");
        }

        sb.AppendLine(BuildStyles(page.Style));
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine(page.Style == HtmlPageStyle.Card ? "    <div class='container'>" : "    <div class='plain'>");

        if (!string.IsNullOrEmpty(icon))
        {
            sb.Append("        <div class='icon' style='color:").Append(color).Append(";'>").Append(icon).AppendLine("</div>");
        }

        sb.Append("        <h1 style='color:").Append(color).Append(";'>").Append(heading).AppendLine("</h1>");
        sb.Append("        <div class='body'>").Append(body).AppendLine("</div>");

        if (page.Redirect is { } r)
        {
            sb.Append("        <p class='hint'>Redirecting in ")
              .Append(r.DelaySeconds)
              .AppendLine(" seconds...</p>");
        }

        if (page.ShowCloseLink)
        {
            sb.AppendLine("        <p class='hint'><a href='javascript:window.close()'>Close this window</a></p>");
        }

        sb.AppendLine("    </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string BuildStyles(HtmlPageStyle style) => style switch
    {
        HtmlPageStyle.Card => """
            <style>
                body { font-family: Arial, sans-serif; display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0; background: #f3f4f6; }
                .container { text-align: center; padding: 40px; background: white; border-radius: 12px; box-shadow: 0 4px 6px rgba(0,0,0,0.1); max-width: 500px; }
                .icon { font-size: 64px; margin-bottom: 20px; }
                h1 { margin: 0 0 10px; }
                .body { color: #6b7280; }
                .body p { margin: 10px 0; }
                .hint { color: #6b7280; font-size: 12px; margin-top: 20px; }
            </style>
            """,
        _ /* Plain */ => """
            <style>
                body { font-family: Arial, sans-serif; text-align: center; padding: 50px; margin: 0; }
                .plain { max-width: 500px; margin: 0 auto; }
                .icon { font-size: 60px; margin-bottom: 20px; }
                h1 { font-size: 1.5em; margin: 0 0 10px; }
                .body { color: #374151; }
                .hint { color: #666; font-size: 12px; margin-top: 20px; }
            </style>
            """,
    };
}
