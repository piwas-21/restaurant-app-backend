using System.Globalization;
using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// The fallback contract for the email resource sets (EMAIL-LOCALISATION-PLAN §6.6).
/// <para>
/// RUMI's registry lists all ten UI languages while only the neutral (English) resources
/// exist, so a resolved culture with no resource set is the NORMAL case, not an edge one.
/// It must render English — never an empty string, which is what a mis-wired lookup
/// silently produces and no type check would catch.
/// </para>
/// </summary>
public class EmailTemplateCultureFallbackTests
{
    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");
    private static readonly CultureInfo NoResources = CultureInfo.GetCultureInfo("fr-CH");

    [Fact]
    public void A_culture_without_resources_renders_the_english_subject()
    {
        var fallback = EmailTemplates.OrderReceived.GetSubject(NoResources, Brand);

        fallback.Should().NotBeNullOrWhiteSpace();
        fallback.Should().Be(EmailTemplates.OrderReceived.GetSubject(EmailCultures.English, Brand));
    }

    [Fact]
    public void A_culture_without_resources_renders_the_english_body()
    {
        var fallback = EmailTemplates.OrderReceived.GetHtmlBody(
            NoResources, Brand, "Jane Doe", "ORD-1", "DineIn", 25.00m, "CHF",
            [("Burger", 2, 12.50m)], "admin@demo.test");

        fallback.Should().Contain("Order Received");
        fallback.Should().Be(EmailTemplates.OrderReceived.GetHtmlBody(
            EmailCultures.English, Brand, "Jane Doe", "ORD-1", "DineIn", 25.00m, "CHF",
            [("Burger", 2, 12.50m)], "admin@demo.test"));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ar")]
    [InlineData("zh-Hans")]
    public void Every_unsupported_culture_falls_back_rather_than_blanking(string language)
    {
        var text = EmailText.For(CultureInfo.GetCultureInfo(language), "Welcome");

        text["PageTitle"].Should().Be("Welcome");
        text["AutomatedMessage"].Should().NotBeNullOrWhiteSpace("it lives in the shared Common set");
    }

    [Fact]
    public void A_key_that_exists_in_no_set_throws_rather_than_rendering_empty()
    {
        var text = EmailText.For(EmailCultures.English, "Welcome");

        var act = () => text["ThisKeyDoesNotExist"];

        act.Should().Throw<InvalidOperationException>().WithMessage("*ThisKeyDoesNotExist*");
    }
}
