using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// Pins the acknowledge flow's email copy (order confirmation flows): when the caller passes a
/// review window the received-mail renders the "received — under review for about N minutes"
/// block INSTEAD of the historical "pending confirmation" copy, and when it does not, the mail
/// is byte-identical to the pre-feature rendering. The window number must appear in both HTML
/// and text bodies — a review promise the guest cannot read the number of is not a promise.
/// </summary>
public class OrderReceivedAcknowledgeTemplateTests
{
    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");
    private static readonly (string name, int quantity, decimal price)[] Items = [("Burger", 2, 12.50m)];

    private static OrderMailDetails Details(int? reviewWindowMinutes) => new(
        "ORD-1",
        "Takeaway",
        25.00m,
        Items,
        "CHF",
        ReviewWindowMinutes: reviewWindowMinutes);

    [Fact]
    public void WithoutWindow_RendersTheHistoricalPendingCopy()
    {
        var html = EmailTemplates.OrderReceived.GetHtmlBody(
            EmailCultures.English, Brand, "Jane Doe", Details(null), "admin@demo.test");
        var text = EmailTemplates.OrderReceived.GetTextBody(
            EmailCultures.English, Brand, "Jane Doe", Details(null), "admin@demo.test");

        html.Should().Contain("Pending Confirmation");
        html.Should().NotContain("under review for about");
        text.Should().Contain("PENDING CONFIRMATION");
        text.Should().NotContain("under review for about");
    }

    [Fact]
    public void WithWindow_RendersTheReviewPromiseWithTheMinutes()
    {
        var html = EmailTemplates.OrderReceived.GetHtmlBody(
            EmailCultures.English, Brand, "Jane Doe", Details(2), "admin@demo.test");
        var text = EmailTemplates.OrderReceived.GetTextBody(
            EmailCultures.English, Brand, "Jane Doe", Details(2), "admin@demo.test");

        html.Should().Contain("review it within about 2 minutes");
        html.Should().NotContain("Pending Confirmation");
        text.Should().Contain("review it within about 2 minutes");
        text.Should().NotContain("PENDING CONFIRMATION");
    }

    [Fact]
    public void FrenchWindow_RendersTheFrenchCopy()
    {
        var html = EmailTemplates.OrderReceived.GetHtmlBody(
            EmailCultures.For("fr"), Brand, "Jane Doe", Details(3), "admin@demo.test");

        html.Should().Contain("sous environ 3 minutes");
    }
}
