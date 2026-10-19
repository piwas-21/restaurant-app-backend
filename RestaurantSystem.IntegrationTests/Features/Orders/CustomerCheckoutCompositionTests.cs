using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutCompositionTests(DatabaseFixture fixture) : CustomerCheckoutCompositionFixture(fixture)
{
    [Fact]
    public async Task Explicit_product_and_ingredient_choices_are_repriced_once_at_the_selected_quantities()
    {
        var basket = await PrepareAsync(new()
        {
            OperationId = Guid.NewGuid(),
            ProductId = ProductId,
            Quantity = 2,
            CustomizationSelections = Options(true, 2),
            SelectedIngredients = [_ingredientId],
            IngredientQuantities = new() { [_ingredientId] = 2 },
            SpecialInstructions = "Keep warm"
        });
        var quote = await QuoteAsync(Review(basket, 0));
        // (7 base + 3 membership + 2 * 1.5 sauce) * 2 parent units.
        quote.Money.SubTotal.Should().Be(26m);
        quote.Money.Total.Should().Be(26m);
        quote.Items[0].SpecialInstructions.Should().Be("Keep warm");
        quote.Items[0].Children.Should().ContainSingle().Which.ProductId.Should().Be(_extraId);
        quote.Items[0].Children[0].Quantity.Should().Be(2);
        quote.Items[0].Children[0].UnitPrice.Should().Be(3m); // Never the supplement's standalone 19.
        quote.Items[0].Children[0].ItemTotal.Should().Be(0m);
        quote.Items[0].Ingredients.Should().Contain(item => item.IngredientId == _ingredientId && !item.IsRemoved);
        (await QuoteAsync(Review(basket, 0))).QuoteFingerprint.Should().Be(quote.QuoteFingerprint);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Bundle_option_customization_preserves_absolute_child_quantities_and_zero_child_totals()
    {
        var basket = await PrepareAsync(new()
        {
            OperationId = Guid.NewGuid(),
            ProductId = _bundleId,
            Quantity = 2,
            SelectedMenuOptions = [new()
            {
                SectionId = _sectionId, ItemId = ProductId, Quantity = 2,
                CustomizationSelections = Options(true, 1), SelectedIngredients = [_ingredientId],
                IngredientQuantities = new() { [_ingredientId] = 1 }, SpecialInstructions = "Sauce separately"
            }]
        });
        var quote = await QuoteAsync(Review(basket, 0));
        // (8 bundle base + 2 * (2 section surcharge + 3 supplement + 1.5 sauce)) * 2 bundles.
        quote.Money.SubTotal.Should().Be(42m);
        var child = quote.Items[0].Children.Should().ContainSingle().Subject;
        child.Quantity.Should().Be(4);
        child.ItemTotal.Should().Be(0m);
        child.SpecialInstructions.Should().Be("Sauce separately");
        child.Children.Should().ContainSingle().Which.Quantity.Should().Be(4);
        child.Children[0].ItemTotal.Should().Be(0m);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Removed_ingredient_membership_refuses_rather_than_silently_dropping_the_selected_extra()
    {
        var basket = await PrepareAsync(new()
        {
            OperationId = Guid.NewGuid(),
            ProductId = ProductId,
            Quantity = 2,
            CustomizationSelections = Options(false, 2)
        });
        (await QuoteAsync(Review(basket, 0))).Money.SubTotal.Should().Be(20m);
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.ProductCustomizationIngredientOptions.Remove(await context.ProductCustomizationIngredientOptions
                .SingleAsync(item => item.Id == _ingredientOptionId));
            await context.SaveChangesAsync();
        }
        (await QuoteResponseAsync(Review(basket))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Removed_membership_and_unavailable_nested_product_refuse_the_original_selection()
    {
        var basket = await PrepareAsync(new()
        {
            OperationId = Guid.NewGuid(),
            ProductId = ProductId,
            Quantity = 2,
            CustomizationSelections = Options(true)
        });
        await QuoteAsync(Review(basket));
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Products.SingleAsync(item => item.Id == _extraId)).IsAvailable = false;
            await context.SaveChangesAsync();
        }
        (await QuoteResponseAsync(Review(basket))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Products.SingleAsync(item => item.Id == _extraId)).IsAvailable = true;
            context.ProductCustomizationProductOptions.Remove(await context.ProductCustomizationProductOptions.SingleAsync(item => item.Id == _optionId));
            await context.SaveChangesAsync();
        }
        (await QuoteResponseAsync(Review(basket))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
    }
}
