using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// Pins the quick-action links in the admin order email.
///
/// The endpoints behind these buttons are [AllowAnonymous] and authenticate the caller purely by
/// the token in the URL (ORDER-TYPE-AVAILABILITY-PLAN §9.20). That makes the template the only
/// place the owner's own links get their credential — a template edit that drops the token is not
/// a cosmetic regression, it silently dead-links every confirm/cancel button in production mail.
/// The email renders a light-mode and a dark-mode copy of the same block, so each assertion counts
/// occurrences rather than merely checking presence: dropping the token from one copy is exactly
/// the edit a "looks fine in my client" review misses.
/// </summary>
public class OrderConfirmationAdminLinkTests
{
    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");
    private static readonly (string name, int quantity, decimal price)[] Items = [("Burger", 2, 12.50m)];

    private const string Token = "quick-action-token-under-test";
    private const string ApiBaseUrl = "https://api.demo.test";

    /// <summary>Light-mode and dark-mode copies of the action block.</summary>
    private const int RenderedCopies = 2;

    private static string Render(string? token = Token, string orderNumber = "ORD-1") =>
        EmailTemplates.OrderConfirmationAdmin.GetHtmlBody(
            Brand, orderNumber, "Jane Doe", "jane@demo.test", "+41000000", "DineIn", 25.00m, "CHF",
            Items, ApiBaseUrl, "https://demo.test", "admin@demo.test", token);

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(45)]
    public void ConfirmLinks_CarryTheQuickActionToken(int minutes)
    {
        var html = Render();

        CountOf(html, $"{ApiBaseUrl}/api/Orders/ORD-1/quick-confirm?token={Token}&amp;minutes={minutes}'")
            .Should().Be(RenderedCopies,
                "each confirm button, in both the light and dark blocks, must carry the token");
    }

    [Fact]
    public void CancelLink_CarriesTheQuickActionToken()
    {
        var html = Render();

        CountOf(html, $"{ApiBaseUrl}/api/Orders/ORD-1/quick-cancel?token={Token}'")
            .Should().Be(RenderedCopies);
    }

    /// <summary>
    /// The pre-fix URL shape. Asserted explicitly because the failure mode is additive — a
    /// tokenless link still renders as a working-looking button, and would still have been an
    /// anonymous enumerable cancel if the endpoint had not also been tightened.
    /// </summary>
    [Fact]
    public void NoLink_IsRenderedWithoutAToken()
    {
        var html = Render();

        html.Should().NotContain("/quick-confirm?minutes=");
        html.Should().NotContain("/quick-cancel'");
    }

    /// <summary>
    /// Orders predating the token column render buttons that resolve to "Order Not Found" rather
    /// than crashing the send — the owner falls back to the dashboard link in the same email.
    /// </summary>
    [Fact]
    public void MissingToken_StillRenders_WithAnEmptyTokenParameter()
    {
        var html = Render(token: null);

        CountOf(html, $"{ApiBaseUrl}/api/Orders/ORD-1/quick-cancel?token='").Should().Be(RenderedCopies);
    }

    /// <summary>An order number is a URL path segment before it is display text.</summary>
    [Fact]
    public void OrderNumber_IsUrlEscaped_InTheLinks()
    {
        var html = Render(orderNumber: "ORD 1/2");

        CountOf(html, "/api/Orders/ORD%201%2F2/quick-cancel").Should().Be(RenderedCopies);
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
