using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// The two operator mails ship a light-mode and a dark-mode copy of their body, because a mail
/// client cannot be asked which scheme it is in — one is hidden by a <c>prefers-color-scheme</c>
/// media query. Backend #356 collapsed each pair into ONE block rendered twice against
/// <c>EmailPalette</c>; this suite pins the property that made the duplication safe to remove.
/// </summary>
/// <remarks>
/// The goldens already prove the bytes did not change. What they cannot see is DRIFT: before this
/// refactor the two copies were 100 lines each, edited by hand, and three differences had already
/// crept in that are not colour (a tinted items-table body, and the spacing and weight of the
/// "confirm with a time" row) — nobody recorded why, and nobody could have noticed. A reader
/// notices a difference in the words; nobody notices one in a hex code. So the assertion here is on
/// the WORDS: whatever a guest's client picks, the operator reads the same mail.
/// </remarks>
public class EmailTemplateColourSchemeTests
{
    private static readonly CultureInfo Culture = EmailCultures.English;
    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");

    [Fact]
    public void The_order_alerts_two_schemes_say_exactly_the_same_thing()
    {
        var html = EmailTemplates.OrderConfirmationAdmin.GetHtmlBody(
            Culture, Brand, "ORD-1", "Jane Doe", "jane@demo.test", "+41000000", "Delivery", 25.00m, "CHF",
            [("Burger", 2, 12.50m)], "https://api.test", "https://app.test", "admin@demo.test",
            "quick-action-token", "No onions", "Rue de Test 1");

        AssertBothSchemesReadTheSame(html);
    }

    [Fact]
    public void The_reservation_alerts_two_schemes_say_exactly_the_same_thing()
    {
        var html = EmailTemplates.ReservationAdminNotification.GetHtmlBody(
            Culture, Brand, new Guid("11111111-2222-3333-4444-555555555555"), "Jane Doe",
            "jane@demo.test", "+41000000", new DateTime(2030, 5, 17, 0, 0, 0, DateTimeKind.Utc),
            new TimeSpan(19, 30, 0), new TimeSpan(21, 0, 0), 4, "T12",
            "https://api.test", "https://app.test", "admin@demo.test", "Window seat");

        AssertBothSchemesReadTheSame(html);
    }

    [Fact]
    public void Both_schemes_are_actually_in_the_document()
    {
        // The premise of the two tests above: if a rename ever left only one block, comparing it to
        // itself would pass and the operator on the other scheme would get a blank mail.
        var html = EmailTemplates.OrderConfirmationAdmin.GetHtmlBody(
            Culture, Brand, "ORD-1", "Jane Doe", "jane@demo.test", "+41000000", "Delivery", 25.00m, "CHF",
            [("Burger", 2, 12.50m)], "https://api.test", "https://app.test", "admin@demo.test", null);

        html.Should().Contain("class='light-only'").And.Contain("class='dark-only'");
        html.Should().Contain("prefers-color-scheme: dark").And.Contain("prefers-color-scheme: light");
    }

    private static void AssertBothSchemesReadTheSame(string html)
    {
        var light = VisibleText(Block(html, "light-only"));
        var dark = VisibleText(Block(html, "dark-only"));

        light.Should().NotBeEmpty("a block with no words in it would make this comparison vacuous");
        dark.Should().Equal(light);
    }

    /// <summary>The one scheme's block: from its wrapper div to the start of the next comment.</summary>
    private static string Block(string html, string schemeClass)
    {
        var start = html.IndexOf($"<div class='{schemeClass}'", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the {0} block has to exist to be compared", schemeClass);

        var next = html.IndexOf("<!-- Dark Mode Version -->", start, StringComparison.Ordinal);
        var end = next > -1 ? next : html.IndexOf("</body>", start, StringComparison.Ordinal);

        return html[start..end];
    }

    /// <summary>
    /// The words, with every tag (and therefore every colour, style and attribute) removed. Runs of
    /// whitespace collapse, because the two blocks are indented independently.
    /// </summary>
    private static string[] VisibleText(string block) =>
        Regex.Replace(block, "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
