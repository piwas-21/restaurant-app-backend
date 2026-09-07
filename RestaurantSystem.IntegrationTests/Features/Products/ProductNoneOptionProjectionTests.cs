using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// Pins the by-id projection of <c>GlobalIngredient.IsNoneOption</c> (the "no X" answer marker,
/// MC FOOD partner feedback 2026-09-07): the guest sheet makes such a row exclusive within its
/// sauce step, so the flag must be TRUE for a live none-option library row and FALSE when that
/// library row is soft-deleted (a deleted global withholds more than its translations — the
/// §9.14 family — and must not keep acting as an answer).
/// </summary>
[Collection("Database Lane 4")]
public class ProductNoneOptionProjectionTests : IntegrationTestBase
{
    private Guid _productId;

    public ProductNoneOptionProjectionTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var category = await context.Categories.OrderBy(c => c.Name).FirstAsync();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "None-option projection",
            BasePrice = 10m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _productId = product.Id;
        product.ProductCategories.Add(new ProductCategory
        {
            ProductId = product.Id,
            CategoryId = category.Id,
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var liveGlobal = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            DefaultName = "Sans Sauces",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            IsNoneOption = true,
        };
        var deletedGlobal = new GlobalIngredient
        {
            Id = Guid.NewGuid(),
            DefaultName = "Deleted none",
            IsActive = true,
            IsDeleted = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            IsNoneOption = true,
        };
        context.GlobalIngredients.AddRange(liveGlobal, deletedGlobal);

        product.DetailedIngredients.Add(new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Name = "Sans Sauces",
            Price = 0m,
            IsOptional = true,
            IsActive = true,
            DisplayOrder = 1,
            Kind = IngredientKind.Sauce,
            GlobalIngredientId = liveGlobal.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        });
        product.DetailedIngredients.Add(new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Name = "Deleted none",
            Price = 0m,
            IsOptional = true,
            IsActive = true,
            DisplayOrder = 2,
            Kind = IngredientKind.Sauce,
            GlobalIngredientId = deletedGlobal.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        });
        product.DetailedIngredients.Add(new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Name = "Garlic",
            Price = 1m,
            IsOptional = true,
            IsActive = true,
            DisplayOrder = 3,
            Kind = IngredientKind.Sauce,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        });

        context.Products.Add(product);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ById_CarriesTheNoneOptionMarker_WithTheSoftDeleteGuard()
    {
        var response = await Client.GetAsync($"/api/Products/{_productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ingredients = body.RootElement
            .GetProperty("data")
            .GetProperty("detailedIngredients")
            .EnumerateArray();

        var byName = ingredients.ToDictionary(
            row => row.GetProperty("name").GetString()!,
            row => row.TryGetProperty("isNoneOption", out var flag) && flag.GetBoolean());

        byName["Sans Sauces"].Should().BeTrue("the live none-option library row projects the marker");
        byName["Deleted none"].Should().BeFalse("a soft-deleted global is not an answer (soft-delete guard)");
        byName["Garlic"].Should().BeFalse("unlinked rows are never none-options");
    }
}
