using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Products;

public class ProductsControllerTests : IntegrationTestBase
{
    public ProductsControllerTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }


    [Fact]
    public async Task GetProducts_ReturnsAllProducts()
    {
        // Act
        var response = await Client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>("/api/products");

        result.Should().NotBeNull();

        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data?.Items.Should().HaveCountGreaterOrEqualTo(2); // From seed data
    }

    // The three cases below pin the IncludeMenus contract the admin catalog's
    // All / Items / Bundles filter relies on (redesign #176, slice 7). Each seeds
    // its own Menu bundle so the assertions hold regardless of seed data.

    [Fact]
    public async Task GetProducts_WithoutIncludeMenus_ExcludesMenuBundles()
    {
        var bundleId = await SeedMenuBundleAsync("Excluded Combo");

        var result = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>("/api/products?pageSize=200");

        result!.Success.Should().BeTrue();
        result.Data!.Items.Should().NotContain(p => p.Id == bundleId,
            "an unfiltered query keeps the customer-catalog default of hiding Menu bundles");
        result.Data.Items.Should().OnlyContain(p => p.Type != ProductType.Menu);
    }

    [Fact]
    public async Task GetProducts_WithIncludeMenus_ReturnsItemsAndMenuBundlesTogether()
    {
        var bundleId = await SeedMenuBundleAsync("Included Combo");

        var result = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>(
            "/api/products?includeMenus=true&pageSize=200");

        result!.Success.Should().BeTrue();
        result.Data!.Items.Should().Contain(p => p.Id == bundleId,
            "IncludeMenus opts the caller into a mixed list");
        result.Data.Items.Should().Contain(p => p.Type != ProductType.Menu,
            "the mixed list still carries plain items — it is a superset, not a swap");
    }

    [Fact]
    public async Task GetProducts_WithExplicitNonMenuType_IgnoresIncludeMenus()
    {
        await SeedMenuBundleAsync("Typed Combo");

        // The discriminating case for the documented "Type wins" precedence: a NON-Menu
        // Type alongside includeMenus=true. Only this direction fails if the precedence
        // is inverted — `type=Menu&includeMenus=false` points both params the same way
        // and survives the inversion it is meant to catch. The chips UI builds params
        // from component state, so a stale includeMenus riding along with a Type chip
        // is exactly this bug's shape.
        var result = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>(
            $"/api/products?type={nameof(ProductType.Beverage)}&includeMenus=true&pageSize=200");

        result!.Success.Should().BeTrue();
        result.Data!.Items.Should().NotBeEmpty("seed data has a Beverage — an empty list would make OnlyContain vacuous");
        result.Data.Items.Should().OnlyContain(p => p.Type == ProductType.Beverage);
    }

    [Fact]
    public async Task GetProducts_WithExplicitMenuType_ReturnsOnlyMenuBundles()
    {
        var bundleId = await SeedMenuBundleAsync("Typed Combo");

        // Guards the narrower inversion: Type=Menu must not be re-narrowed by includeMenus=false.
        var result = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>(
            $"/api/products?type={nameof(ProductType.Menu)}&includeMenus=false&pageSize=200");

        result!.Success.Should().BeTrue();
        result.Data!.Items.Should().Contain(p => p.Id == bundleId);
        result.Data.Items.Should().OnlyContain(p => p.Type == ProductType.Menu);
    }

    private async Task<Guid> SeedMenuBundleAsync(string name)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var bundle = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Seeded by ProductsControllerTests",
            BasePrice = 20.00m,
            IsActive = true,
            IsAvailable = true,
            PreparationTimeMinutes = 15,
            Type = ProductType.Menu,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            DisplayOrder = 99,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        context.Products.Add(bundle);
        await context.SaveChangesAsync();

        return bundle.Id;
    }
}
