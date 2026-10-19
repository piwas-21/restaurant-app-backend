using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutBundleIdentityTests(DatabaseFixture fixture) : CustomerCheckoutCompositionFixture(fixture)
{
    [Fact]
    public async Task Chosen_component_variation_is_distinct_from_a_menu_rows_fixed_variation()
    {
        Guid variationId;
        Guid rowId;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var variation = new ProductVariation
            {
                ProductId = ProductId,
                Name = "Large",
                PriceModifier = 4m,
                CreatedBy = "quote-fixture"
            };
            context.ProductVariations.Add(variation);
            await context.SaveChangesAsync();
            variationId = variation.Id;
            rowId = await context.MenuSectionItems.Where(row => row.MenuSectionId == _sectionId).Select(row => row.Id).SingleAsync();
        }
        var basket = await PrepareAsync(new()
        {
            OperationId = Guid.NewGuid(),
            ProductId = _bundleId,
            Quantity = 2,
            SelectedMenuOptions = [new()
            {
                SectionId = _sectionId, MenuSectionItemId = rowId, ItemId = ProductId,
                ComponentProductVariationId = variationId, Quantity = 1, CustomizationSelections = Options(false)
            }]
        });
        var quote = await QuoteAsync(Review(basket, 0));
        quote.Money.SubTotal.Should().Be(28m); // (8 base + 2 row surcharge + 4 chosen variation) * 2.
        quote.Items[0].Children.Should().ContainSingle().Which.ProductVariationId.Should().Be(variationId);
        quote.Items[0].Children[0].Quantity.Should().Be(2);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Exact_menu_row_identity_preserves_duplicate_authored_products_and_their_distinct_surcharges()
    {
        Guid rowId;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = new MenuSectionItem
            {
                MenuSectionId = _sectionId,
                ProductId = ProductId,
                AdditionalPrice = 5m,
                CreatedBy = "quote-fixture"
            };
            context.MenuSectionItems.Add(row);
            await context.SaveChangesAsync();
            rowId = row.Id;
        }
        var basket = await PrepareAsync(new()
        {
            OperationId = Guid.NewGuid(),
            ProductId = _bundleId,
            Quantity = 2,
            SelectedMenuOptions = [new()
            {
                SectionId = _sectionId, MenuSectionItemId = rowId, ItemId = ProductId,
                Quantity = 1, CustomizationSelections = Options(false)
            }]
        });
        (await QuoteAsync(Review(basket, 0))).Money.SubTotal.Should().Be(26m); // (8 + selected row 5) * 2.
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.MenuSectionItems.Remove(await context.MenuSectionItems.SingleAsync(row => row.Id == rowId));
            await context.SaveChangesAsync();
        }
        (await QuoteResponseAsync(Review(basket))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
    }
}
