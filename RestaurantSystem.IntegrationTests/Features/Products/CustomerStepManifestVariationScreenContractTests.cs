using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using System.Net;
using System.Text.Json.Nodes;

namespace RestaurantSystem.IntegrationTests.Features.Products;

public partial class CustomerStepManifestContractTests
{
    [Fact]
    public async Task VariationAlternativesMustShareOneScreenPerSelectionOwner()
    {
        AuthenticateAsAdmin();
        var secondVariationId = Guid.NewGuid();
        var firstSauceId = Guid.NewGuid();
        var secondSauceId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.ProductVariations.Add(new ProductVariation
            {
                Id = secondVariationId,
                ProductId = _productId,
                Name = "Large",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });
            context.ProductIngredients.AddRange(
                NewSauce(firstSauceId, _productId, "Mild sauce"),
                NewSauce(secondSauceId, _productId, "Hot sauce"));
            await context.SaveChangesAsync();
        }

        var splitScreens = Manifest(0,
            VariationStep(0),
            new JsonObject
            {
                ["kind"] = "productVariation",
                ["targetId"] = secondVariationId,
                ["compositionRole"] = "requiredChoice",
                ["presentationOrder"] = 1
            });
        (await PutAsync(splitScreens)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadManifestAsync()).Should().BeNull();

        var splitSauceScreens = Manifest(0,
            VariationStep(0),
            new JsonObject
            {
                ["kind"] = "productVariation",
                ["targetId"] = secondVariationId,
                ["compositionRole"] = "requiredChoice",
                ["presentationOrder"] = 0
            },
            SauceStep(firstSauceId, 1), SauceStep(secondSauceId, 2));
        (await PutAsync(splitSauceScreens)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadManifestAsync()).Should().BeNull();

        (await ReadManifestAsync()).Should().BeNull();
    }

    private static ProductIngredient NewSauce(Guid id, Guid productId, string name) => new()
    {
        Id = id,
        ProductId = productId,
        Name = name,
        Kind = IngredientKind.Sauce,
        IsOptional = true,
        IsIncludedInBasePrice = true,
        IsActive = true,
        MaxQuantity = 1,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test"
    };

    private static JsonObject SauceStep(Guid id, int order) => new()
    {
        ["kind"] = "productSauce",
        ["targetId"] = id,
        ["compositionRole"] = "sauce",
        ["presentationOrder"] = order
    };
}
