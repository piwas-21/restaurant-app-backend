using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class CustomerCheckoutFingerprintTests
{
    private static CustomerCheckoutQuoteDto Quote()
    {
        var ingredient = new CustomerCheckoutIngredientDto(Guid.Parse("44444444-4444-4444-8444-444444444444"),
            "Sauce", 0, true, false, QuantityBasis.PerParentUnit, ConfigurationScope.SharedAcrossParentUnits,
            CompositionRole.Sauce, 1);
        var line = new CustomerCheckoutLineDto(Guid.Parse("22222222-2222-4222-8222-222222222222"),
            Guid.Parse("33333333-3333-4333-8333-333333333333"), null, null, "Meal", null, 2, 7m, 14m,
            "No salt", null, QuantityBasis.LineTotal, ConfigurationScope.SharedAcrossParentUnits,
            CompositionRole.Dish, "Main", 1, [ingredient], []);
        return new(1, Guid.Parse("11111111-1111-4111-8111-111111111111"), new string('a', 64), "ignored",
            OrderType.Takeaway, "CHF", false, new(14m, 0m, 0m, 0m, 0m, 0m, 0, 3m, 17m), [line],
            [PaymentMethod.Cash, PaymentMethod.CreditCard]);
    }

    private static CustomerCheckoutReviewDto Review(int points = 0) => new() { PointsToRedeem = points };
    private static string Digest(CustomerCheckoutQuoteDto quote) =>
        CustomerCheckoutQuoteFingerprint.Compute(quote, Review(), null);

    [Fact]
    public void Canonical_quote_has_an_independently_derived_SHA256_golden()
    {
        // Independent Python json.dumps(sort_keys=True,separators=(',',':')) + hashlib.sha256,
        // with explicit numeric enum values and invariant strings for every money field.
        Digest(Quote()).Should().Be("cd91159bab6d01fd9b3e071a7ff656a5df0c0347ad6b89b86ab20752e416da4b"); // pragma: allowlist secret
    }

    [Fact]
    public void Decimal_scale_and_nonfinancial_quote_metadata_do_not_change_a_review()
    {
        var quote = Quote();
        var equivalent = quote with
        {
            PriceChanged = true,
            QuoteFingerprint = "transient",
            Money = quote.Money with { SubTotal = 14.000m, Tip = 3.00m, Total = 17.0m },
            Items = [quote.Items[0] with { UnitPrice = 7.000m, ItemTotal = 14.00m }],
            AvailablePaymentMethods = [PaymentMethod.CreditCard, PaymentMethod.Cash]
        };
        Digest(equivalent).Should().Be(Digest(quote));
    }

    [Fact]
    public void Opposing_line_changes_cannot_hide_behind_an_unchanged_total()
    {
        var quote = Quote();
        var second = quote.Items[0] with { BasketItemId = Guid.NewGuid(), Name = "Second meal" };
        quote = quote with { Items = [quote.Items[0], second] };
        var changed = quote with { Items = [quote.Items[0] with { ItemTotal = 13m }, second with { ItemTotal = 15m }] };
        changed.Money.Should().Be(quote.Money);
        Digest(changed).Should().NotBe(Digest(quote));
        Digest(quote with { Items = quote.Items.Reverse().ToArray() }).Should().Be(Digest(quote));
    }

    [Fact]
    public void Identity_currency_customer_choices_and_payment_availability_are_bound_to_review()
    {
        var quote = Quote();
        var changed = new[]
        {
            quote with { BasketId = Guid.NewGuid() }, quote with { Currency = "EUR" },
            quote with { PurchaseFingerprint = new string('b', 64) }, quote with { Type = OrderType.Delivery },
            quote with { AvailablePaymentMethods = [PaymentMethod.Cash] },
            quote with { Money = quote.Money with { Tip = 4m, Total = 18m } },
            quote with { Items = [quote.Items[0] with { SpecialInstructions = "" }] },
            quote with { Items = [quote.Items[0] with { Ingredients = [] }] }
        };
        foreach (var candidate in changed) Digest(candidate).Should().NotBe(Digest(quote));
        CustomerCheckoutQuoteFingerprint.Compute(quote, Review(1), null).Should().NotBe(Digest(quote));
        CustomerCheckoutQuoteFingerprint.Compute(quote, Review(), Guid.NewGuid()).Should().NotBe(Digest(quote));
    }

    [Fact]
    public void Nested_composition_is_bound_but_sibling_enumeration_order_is_irrelevant()
    {
        var quote = Quote();
        var child = quote.Items[0] with { BasketItemId = null, Name = "Option", ItemTotal = 0m };
        var second = child with { Name = "Second option", UnitPrice = 2m };
        quote = quote with { Items = [quote.Items[0] with { Children = [child, second] }] };
        Digest(quote with { Items = [quote.Items[0] with { Children = [second, child] }] }).Should().Be(Digest(quote));
        var nested = quote with { Items = [quote.Items[0] with { Children = [child with { Children = [second] }] }] };
        Digest(nested).Should().NotBe(Digest(quote));
    }
}
