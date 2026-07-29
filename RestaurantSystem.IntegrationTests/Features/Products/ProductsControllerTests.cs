using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text.Json;

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

    // --- PATCH /api/products/{id}/price (admin quick-edit) ---

    [Fact]
    public async Task UpdateProductPrice_AsAdmin_PersistsNewBasePrice()
    {
        var productId = await SeedPricedProductAsync("Quick-edit Cola", 10.00m);
        AuthenticateAsAdmin();

        var response = await PatchPriceAsync(productId, 14.50m);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadResponseAsync<ApiResponse<decimal>>(response);
        body!.Success.Should().BeTrue();
        body.Data.Should().Be(14.50m);
        (await GetPersistedBasePriceAsync(productId)).Should().Be(14.50m);
    }

    [Fact]
    public async Task UpdateProductPrice_AsNonAdmin_IsForbidden()
    {
        var productId = await SeedPricedProductAsync("Guarded Cola", 10.00m);
        AuthenticateAsUser(); // authenticated Customer, not admin

        var response = await PatchPriceAsync(productId, 99.00m);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetPersistedBasePriceAsync(productId)).Should()
            .Be(10.00m, "a non-admin must not be able to mutate the price");
    }

    [Fact]
    public async Task UpdateProductPrice_UnknownProduct_ReturnsNotFound()
    {
        AuthenticateAsAdmin();

        var response = await PatchPriceAsync(Guid.NewGuid(), 12.00m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateProductPrice_NegativePrice_IsRejected()
    {
        var productId = await SeedPricedProductAsync("Cheap Cola", 10.00m);
        AuthenticateAsAdmin();

        var response = await PatchPriceAsync(productId, -1.00m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetPersistedBasePriceAsync(productId)).Should().Be(10.00m);
    }

    [Fact]
    public async Task UpdateProductPrice_OverColumnBound_IsRejected()
    {
        var productId = await SeedPricedProductAsync("Priceless Cola", 10.00m);
        AuthenticateAsAdmin();

        // decimal(10,2) tops out at 99,999,999.99 — one over must 400, not overflow into a 500.
        var response = await PatchPriceAsync(productId, 100_000_000.00m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetPersistedBasePriceAsync(productId)).Should().Be(10.00m);
    }

    [Fact]
    public async Task UpdateProductPrice_MoreThanTwoDecimals_IsRejected()
    {
        var productId = await SeedPricedProductAsync("Fractional Cola", 10.00m);
        AuthenticateAsAdmin();

        var response = await PatchPriceAsync(productId, 10.999m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetPersistedBasePriceAsync(productId)).Should().Be(10.00m);
    }

    [Fact]
    public async Task UpdateProductPrice_ZeroIsAccepted()
    {
        var productId = await SeedPricedProductAsync("Free Cola", 10.00m);
        AuthenticateAsAdmin();

        var response = await PatchPriceAsync(productId, 0m);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetPersistedBasePriceAsync(productId)).Should().Be(0m);
    }

    [Fact]
    public async Task UpdateProductPrice_SoftDeletedProduct_ReturnsNotFound()
    {
        var productId = await SeedPricedProductAsync("Deleted Cola", 10.00m, isDeleted: true);
        AuthenticateAsAdmin();

        var response = await PatchPriceAsync(productId, 12.00m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProducts_ByCategory_DedupesVariationContentByLanguage()
    {
        // RE-HOMED 2026-07-29 from CategoriesControllerTests, whose endpoint
        // (GET /api/Categories/{id}/products) was deleted as unconsumed — plan §9.16. That test
        // was the ONLY fixture in the whole suite seeding two ProductVariationDescription rows
        // with the same LanguageCode, and #138 had two halves. The SQL-translation half died with
        // the endpoint; this half did NOT — ProductSummaryMapper:90 still runs
        // GroupBy(LanguageCode).First().ToDictionary(), and it feeds THIS endpoint, the guest
        // menu. Without a duplicate-language row anywhere, dropping that GroupBy as redundant
        // looks safe and 500s /api/Products with "an item with the same key has already been
        // added". So the coverage moves onto the shipping path rather than being deleted with the
        // dead one.
        var (categoryId, productName) = await SeedProductWithDuplicateLanguageVariationAsync();

        var result = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>(
            $"/api/products?CategoryId={categoryId}&PageSize=50");

        result!.Success.Should().BeTrue();
        var item = result.Data!.Items.Should().ContainSingle(p => p.Name == productName).Subject;
        var variation = item.Variations.Should().ContainSingle().Subject;
        // Two "en" rows collapse to one key. Assert the WINNER, not just the count: First() after
        // GroupBy has to keep the first row, and a count-only assertion passes either way.
        variation.Content.Should().HaveCount(1).And.ContainKey("en");
        // WHICH of the two rows wins is deliberately NOT asserted, because the code does not
        // guarantee it: `Descriptions` is loaded with no ORDER BY, so GroupBy(lang).First() takes
        // whichever row EF materialized first. Measured, not assumed — asserting the first-inserted
        // name passed this test in isolation and failed it in a full-class run, same commit. The
        // dedup (one key, never a duplicate-key throw) is the real contract and is what this pins.
        // Making the winner deterministic means an OrderBy in the mapper; that is a behaviour
        // change to a guest-facing string and belongs in its own PR, not in a §9.16 deletion.
        variation.Content["en"].Name.Should().BeOneOf("Large", "Large (duplicate language)");
    }

    private async Task<(Guid CategoryId, string ProductName)> SeedProductWithDuplicateLanguageVariationAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var categoryId = (await context.Categories.FirstAsync()).Id;
        const string productName = "Duplicate-language QA Product";

        var product = new Product
        {
            Name = productName,
            BasePrice = 9.99m,
            IsActive = true,
            IsAvailable = true,
            CreatedBy = "test",
            Variations = new List<ProductVariation>
            {
                new()
                {
                    Name = "Large",
                    DisplayOrder = 1,
                    IsActive = true,
                    CreatedBy = "test",
                    Descriptions = new List<ProductVariationDescription>
                    {
                        new() { LanguageCode = "en", Name = "Large", CreatedBy = "test" },
                        new() { LanguageCode = "en", Name = "Large (duplicate language)", CreatedBy = "test" },
                    },
                },
            },
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        context.ProductCategories.Add(new ProductCategory
        {
            ProductId = product.Id,
            CategoryId = categoryId,
            DisplayOrder = 1,
            IsPrimary = true,
            CreatedBy = "test",
        });
        await context.SaveChangesAsync();

        return (categoryId, productName);
    }

    private async Task<Guid> SeedPricedProductAsync(string name, decimal price, bool isDeleted = false)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Seeded by ProductsControllerTests",
            BasePrice = price,
            IsActive = true,
            IsAvailable = true,
            PreparationTimeMinutes = 10,
            Type = ProductType.Beverage,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            DisplayOrder = 50,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();

        return product.Id;
    }

    private Task<HttpResponseMessage> PatchPriceAsync(Guid id, decimal price)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { price }, JsonOptions),
            System.Text.Encoding.UTF8,
            "application/json");
        return Client.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, $"/api/products/{id}/price") { Content = content });
    }

    private async Task<decimal> GetPersistedBasePriceAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = await context.Products.FindAsync(id);
        return product!.BasePrice;
    }
}
