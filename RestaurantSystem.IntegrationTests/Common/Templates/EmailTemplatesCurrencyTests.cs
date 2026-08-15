using FluentAssertions;
using RestaurantSystem.Api.Common.Templates;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.IntegrationTests.Common.Templates;

/// <summary>
/// Pins per-tenant server-side currency in order emails. <see cref="LocalizationSettings"/>
/// defaults to "CHF" so an unconfigured install (the legacy RUMI tenant) renders
/// byte-identical output to the pre-change hardcoded literal; a provisioned
/// tenant supplies its own currency via Localization__Currency (mapped from
/// TENANT_CURRENCY by the deploy repo) and that value — not a hardcoded "CHF" —
/// must flow into both the admin and customer order-confirmation email bodies.
///
/// Still true after the culture parameter landed (EMAIL-LOCALISATION-PLAN §6.2): the culture
/// selects wording only. Amounts keep their ambient F2 formatting and the currency label keeps
/// coming from <see cref="LocalizationSettings.Currency"/> — a culture must never derive a
/// currency, or a French-speaking guest of a Swiss restaurant is quoted in euros.
/// </summary>
public class EmailTemplatesCurrencyTests
{
    private static readonly EmailBranding Brand = new("Demo Restaurant", "Geneva", "contact@demo.test");
    private static readonly (string name, int quantity, decimal price)[] Items = [("Burger", 2, 12.50m)];
    private const decimal Total = 25.00m;
    private const decimal ItemPrice = 12.50m;

    // Templates format amounts with the ambient F2 format (no explicit culture,
    // matching production code), so the expected literal must be built the same
    // way rather than hardcoded as "25.00" — CI/dev machines may run under a
    // non-en-US culture with a comma decimal separator.
    private static string Amount(decimal value) => $"{value:F2}";

    [Fact]
    public void LocalizationSettings_Unconfigured_DefaultsToChf()
    {
        // No Localization section bound (fresh instance) => legacy RUMI install.
        new LocalizationSettings().Currency.Should().Be("CHF");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void LocalizationSettings_BlankCurrency_FallsBackToChf(string blank)
    {
        // An empty Localization__Currency (an empty TENANT_CURRENCY) must not
        // render a blank currency in emails — it falls back to CHF.
        new LocalizationSettings { Currency = blank }.Currency.Should().Be("CHF");
    }

    [Theory]
    [InlineData("CHF")]
    [InlineData("EUR")]
    public void OrderReceived_HtmlAndTextBody_UseConfiguredCurrency_NotHardcodedChf(string currency)
    {
        var htmlBody = EmailTemplates.OrderReceived.GetHtmlBody(
            EmailCultures.English, Brand, "Jane Doe", "ORD-1", "DineIn", Total, currency, Items, "admin@demo.test");
        var textBody = EmailTemplates.OrderReceived.GetTextBody(
            EmailCultures.English, Brand, "Jane Doe", "ORD-1", "DineIn", Total, currency, Items, "admin@demo.test");

        AssertCurrencyRendered(htmlBody, textBody, currency);
    }

    [Theory]
    [InlineData("CHF")]
    [InlineData("EUR")]
    public void OrderConfirmationAdmin_HtmlAndTextBody_UseConfiguredCurrency_NotHardcodedChf(string currency)
    {
        var htmlBody = EmailTemplates.OrderConfirmationAdmin.GetHtmlBody(
            EmailCultures.English, Brand, "ORD-1", "Jane Doe", "jane@demo.test", "+41000000", "DineIn", Total, currency, Items,
            "https://api.demo.test", "https://demo.test", "admin@demo.test", "test-quick-action-token");
        var textBody = EmailTemplates.OrderConfirmationAdmin.GetTextBody(
            EmailCultures.English, Brand, "ORD-1", "Jane Doe", "jane@demo.test", "+41000000", "DineIn", Total, currency, Items,
            "admin@demo.test");

        AssertCurrencyRendered(htmlBody, textBody, currency);
    }

    private static void AssertCurrencyRendered(string htmlBody, string textBody, string currency)
    {
        htmlBody.Should().Contain($"{currency} {Amount(Total)}").And.Contain($"{currency} {Amount(ItemPrice)}");
        textBody.Should().Contain($"{currency} {Amount(Total)}").And.Contain($"{currency} {Amount(ItemPrice)}");

        if (currency != "CHF")
        {
            htmlBody.Should().NotContain("CHF");
            textBody.Should().NotContain("CHF");
        }
    }
}
