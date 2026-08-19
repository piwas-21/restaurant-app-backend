using System.Globalization;
using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// The fallback contract for the email resource sets (EMAIL-LOCALISATION-PLAN §6.6).
/// <para>
/// RUMI's registry lists all ten UI languages while only English, French and German are
/// translated (S1, S7, S8), so a resolved culture with no resource set is the NORMAL case, not an
/// edge one.
/// It must render English — never an empty string, which is what a mis-wired lookup
/// silently produces and no type check would catch.
/// <para>
/// The untranslated culture used here is <b>Italian</b>, and it has to be one the product has
/// genuinely not shipped: this suite asserted the fallback with <c>fr</c> until S7 translated it,
/// at which point the assertions read "French renders English" and were correct only by accident.
/// When Italian ships, move these to the next untranslated locale rather than deleting them —
/// what is being pinned is the fallback, not the language.
/// </para>
/// </para>
/// </summary>
public class EmailTemplateCultureFallbackTests
{
    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");
    private static readonly CultureInfo NoResources = CultureInfo.GetCultureInfo("it-CH");

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
            NoResources, Brand, "Jane Doe", new OrderMailDetails("ORD-1", "DineIn", 25.00m, "CHF", [("Burger", 2, 12.50m)]), "admin@demo.test");

        fallback.Should().Contain("Order Received");
        fallback.Should().Be(EmailTemplates.OrderReceived.GetHtmlBody(
            EmailCultures.English, Brand, "Jane Doe", new OrderMailDetails("ORD-1", "DineIn", 25.00m, "CHF", [("Burger", 2, 12.50m)]), "admin@demo.test"));
    }

    [Theory]
    [InlineData("it")]
    [InlineData("nl")]
    [InlineData("ar")]
    [InlineData("zh-Hans")]
    public void Every_untranslated_culture_falls_back_rather_than_blanking(string language)
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
