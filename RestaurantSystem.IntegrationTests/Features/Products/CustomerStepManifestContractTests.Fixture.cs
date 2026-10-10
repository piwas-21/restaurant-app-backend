using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace RestaurantSystem.IntegrationTests.Features.Products;

public partial class CustomerStepManifestContractTests
{
    private Guid _productId;
    private Guid _categoryId;
    private Guid _variationId;
    private Guid _foreignVariationId;
    private Guid _sideProductId;
    private Guid _sideAssociationId;

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _categoryId = (await context.Categories.OrderBy(row => row.Name).FirstAsync()).Id;
        var product = NewProduct("Seeded screen order product");
        var side = NewProduct("Screen order drink");
        var foreign = NewProduct("Unrelated product");
        _productId = product.Id;
        _sideProductId = side.Id;
        _variationId = Guid.NewGuid();
        _foreignVariationId = Guid.NewGuid();
        _sideAssociationId = Guid.NewGuid();
        product.Variations.Add(new ProductVariation
        {
            Id = _variationId,
            ProductId = product.Id,
            Name = "Regular",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        foreign.Variations.Add(new ProductVariation
        {
            Id = _foreignVariationId,
            ProductId = foreign.Id,
            Name = "Foreign",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        product.SuggestedSideItems.Add(new ProductSideItem
        {
            Id = _sideAssociationId,
            MainProductId = product.Id,
            SideItemProductId = side.Id,
            SideItemProduct = side,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        context.Products.AddRange(product, side, foreign);
        await context.SaveChangesAsync();
    }

    private static Product NewProduct(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        BasePrice = 12m,
        Type = ProductType.MainItem,
        IsActive = true,
        IsAvailable = true,
        Ingredients = [],
        Allergens = [],
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test"
    };

    private JsonObject VariationStep(int order) => new()
    {
        ["kind"] = "productVariation",
        ["targetId"] = _variationId,
        ["compositionRole"] = "requiredChoice",
        ["presentationOrder"] = order
    };

    private JsonObject SideStep(int order) => new()
    {
        ["kind"] = "productSuggestedSide",
        ["targetId"] = _sideAssociationId,
        ["compositionRole"] = "drink",
        ["presentationOrder"] = order
    };

    private static JsonObject Manifest(int revision, params JsonObject[] steps) => new()
    {
        ["schemaVersion"] = 1,
        ["revision"] = revision,
        ["steps"] = new JsonArray(steps.Cast<JsonNode>().ToArray())
    };

    private async Task<HttpResponseMessage> PutAsync(JsonObject? manifest,
        bool includeManifest = true, string name = "Screen order product")
    {
        // Explicit variations and sides preserve the catalogue membership under a full replacement PUT.
        var payload = new JsonObject
        {
            ["id"] = _productId,
            ["name"] = name,
            ["basePrice"] = 12,
            ["isActive"] = true,
            ["isAvailable"] = true,
            ["isSpecial"] = false,
            ["preparationTimeMinutes"] = 10,
            ["type"] = "mainItem",
            ["kitchenType"] = "none",
            ["displayOrder"] = 0,
            ["categoryIds"] = new JsonArray(JsonValue.Create(_categoryId)),
            ["primaryCategoryId"] = _categoryId,
            ["suggestedSideItemIds"] = new JsonArray(JsonValue.Create(_sideProductId)),
            ["variations"] = new JsonArray(new JsonObject
            {
                ["id"] = _variationId,
                ["name"] = "Regular",
                ["priceModifier"] = 0,
                ["isActive"] = true,
                ["displayOrder"] = 0
            })
        };
        if (includeManifest) payload["customerStepManifest"] = manifest;
        return await Client.PutAsync($"/api/Products/{_productId}",
            new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    }

    private async Task<JsonNode?> ReadManifestAsync()
    {
        var response = await Client.GetAsync($"/api/Products/{_productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["success"]!.GetValue<bool>().Should().BeTrue();
        return body["data"]!["customerStepManifest"]?.DeepClone();
    }
}
