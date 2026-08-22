using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// The server half of "hide the base product" (Track F / F2). Hiding the radio client-side is
// presentation; this is the enforcement, and the DEGRADE is as load-bearing as the rejection —
// a product whose variations are all off must stay orderable rather than become unbuyable.
// Pure unit test — no DB.
public class BasketBaseProductGuardTests
{
    private const string Actor = "base-product-guard-tests";

    private static ProductVariation Variation(bool isActive = true) =>
        new() { Id = Guid.NewGuid(), Name = "Revani", IsActive = isActive, CreatedBy = Actor };

    private static Product Product(bool hideBaseProduct, params ProductVariation[] variations)
    {
        var product = new Product { Name = "Günün tatlısı", HideBaseProduct = hideBaseProduct, CreatedBy = Actor };
        foreach (var variation in variations)
        {
            product.Variations.Add(variation);
        }

        return product;
    }

    [Fact]
    public void Blocks_a_bare_add_when_the_base_row_is_hidden()
    {
        var product = Product(hideBaseProduct: true, Variation());

        var act = () => BasketBaseProductGuard.EnsureVariationChosen(product, variation: null);

        act.Should()
            .Throw<BadRequestException>()
            .WithMessage("Günün tatlısı must be ordered with one of its options.");
    }

    [Fact]
    public void Tags_the_rejection_with_a_code_so_the_client_can_re_open_the_picker()
    {
        // A stale tab is the expected way to get here, so the client needs to tell THIS 400 apart
        // from every other one on the endpoint without substring-matching English prose.
        var product = Product(hideBaseProduct: true, Variation());

        var act = () => BasketBaseProductGuard.EnsureVariationChosen(product, variation: null);

        act.Should().Throw<BadRequestException>().Which.ErrorCode.Should().Be(ErrorCodes.VariationRequired);
    }

    [Fact]
    public void Permits_the_add_when_a_variation_was_chosen()
    {
        var chosen = Variation();
        var product = Product(hideBaseProduct: true, chosen);

        var act = () => BasketBaseProductGuard.EnsureVariationChosen(product, chosen);

        act.Should().NotThrow();
    }

    [Fact]
    public void Permits_the_bare_add_for_every_product_that_did_not_ask_to_hide_its_base()
    {
        // The default, and therefore the whole existing catalogue.
        var product = Product(hideBaseProduct: false, Variation());

        var act = () => BasketBaseProductGuard.EnsureVariationChosen(product, variation: null);

        act.Should().NotThrow();
    }

    [Fact]
    public void Degrades_to_permissive_when_every_variation_has_been_deactivated()
    {
        // Otherwise deactivating the last variation leaves the product with ZERO orderable options
        // and no error anyone can act on. The flag hides a row; it must never delete the product.
        var product = Product(hideBaseProduct: true, Variation(isActive: false));

        var act = () => BasketBaseProductGuard.EnsureVariationChosen(product, variation: null);

        act.Should().NotThrow();
    }

    [Fact]
    public void Degrades_to_permissive_when_the_product_has_no_variations_at_all()
    {
        var product = Product(hideBaseProduct: true);

        var act = () => BasketBaseProductGuard.EnsureVariationChosen(product, variation: null);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void The_effective_rule_is_the_stored_flag_AND_an_active_variation(
        bool hideBaseProduct, bool variationIsActive, bool expected)
    {
        var product = Product(hideBaseProduct, Variation(variationIsActive));

        BaseProductVisibility.IsBaseHidden(product).Should().Be(expected);
    }
}
