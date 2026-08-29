using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using FeaturedQuery = RestaurantSystem.Api.Features.Products.Queries.GetFeaturedSpecialQuery.GetFeaturedSpecialQuery;
using ProductsQuery = RestaurantSystem.Api.Features.Products.Queries.GetProductsQuery.GetProductsQuery;
using SpecialsQuery = RestaurantSystem.Api.Features.Products.Queries.GetSpecialProductsQuery.GetSpecialProductsQuery;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// kebabdilhan G5 — the LISTING half of <c>Product.IsComponent</c>: a bundle-only item is not a
/// catalogue item, so it is excluded from every product listing by default, with one admin opt-in.
/// </summary>
/// <remarks>
/// <para>
/// Hiding the card is presentation, not the rule — the rule is
/// <c>BasketComponentGuard</c> (<see cref="Basket.BasketComponentProductTests"/>). These tests exist
/// because the two halves fail INDEPENDENTLY: a listing that leaks components puts an unorderable
/// card on the menu, and an opt-in that does not work leaves the admin unable to see or edit the
/// items they just created.
/// </para>
/// <para>
/// Every assertion here carries a POSITIVE CONTROL in the same call — an ordinary product that must
/// still be present. An exclusion test whose query returned nothing at all would otherwise pass for
/// the wrong reason, which is the standing failure mode of "assert the empty result".
/// </para>
/// </remarks>
[Collection("Database Lane 3")]
public class ComponentProductCatalogTests : IntegrationTestBase
{
    public ComponentProductCatalogTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const string Actor = "g5-catalog-test";
    private const string MeatName = "G5 Viande Poulet";
    private const string PlainName = "G5 Plat Ordinaire";
    private const string ComponentSpecialName = "G5 Viande Marquee Speciale";
    private const string PlainSpecialName = "G5 Speciale Ordinaire";

    private static readonly Guid MeatId = Guid.NewGuid();
    private static readonly Guid PlainId = Guid.NewGuid();

    // ---- the default exclusion ------------------------------------------------------------------

    [Fact]
    public async Task The_product_list_excludes_components_by_default()
    {
        var page = await FetchProductsAsync(includeComponents: false);

        page.Items.Should().NotContain(p => p.Name == MeatName, "a meat option is not a menu card");
        page.Items.Should().Contain(p => p.Name == PlainName, "positive control: ordinary items are untouched");
    }

    [Fact]
    public async Task The_total_count_excludes_components_too()
    {
        // TotalCount comes from the same filtered query, so a paginator built on it cannot
        // advertise rows the list will not serve. The ABSOLUTE number is asserted, not
        // `TotalCount == Items.Count` — both of those move together and would pass unfixed.
        var page = await FetchProductsAsync(includeComponents: false);

        page.TotalCount.Should().Be(
            2, "the seed holds 4 products, and 2 of them are components; pre-fix this was 4");
    }

    [Fact]
    public async Task The_admin_opt_in_brings_them_back()
    {
        var page = await FetchProductsAsync(includeComponents: true);

        page.Items.Should().Contain(p => p.Name == MeatName, "the admin list and the bundle picker need them");
        page.Items.Should().Contain(p => p.Name == PlainName);
        page.TotalCount.Should().Be(4);
    }

    /// <summary>
    /// Everything above drives the handler through the mediator, which cannot see the HTTP binding.
    /// A renamed or dropped <c>[FromQuery]</c> parameter would leave those green while the endpoint
    /// silently ignored the opt-in — a feature that is simply inert, with nothing throwing. The wire
    /// name is what the frontend sends.
    /// </summary>
    [Fact]
    public async Task The_opt_in_binds_from_the_query_string_not_just_the_handler()
    {
        var withOptIn = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>(
            "/api/Products?includeComponents=true&pageSize=50");
        var without = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>(
            "/api/Products?pageSize=50");

        withOptIn!.Data!.Items.Should().Contain(p => p.Name == MeatName);
        without!.Data!.Items.Should().NotContain(p => p.Name == MeatName);
        without.Data.Items.Should().Contain(p => p.Name == PlainName, "positive control on the same call");
    }

    // ---- the guest surfaces, which have no opt-in ------------------------------------------------

    [Fact]
    public async Task The_specials_list_excludes_a_component_and_has_no_opt_in_to_reverse_it()
    {
        var specials = await FetchSpecialsAsync();

        specials.Items.Should().NotContain(p => p.Name == ComponentSpecialName);
        specials.Items.Should().Contain(p => p.Name == PlainSpecialName, "positive control");
        specials.TotalCount.Should().Be(1, "the seed marks two products special and one is a component");
    }

    [Fact]
    public async Task The_featured_banner_never_shows_a_component()
    {
        // The banner is an ENTRY POINT — a guest orders straight from it — so a component reaching
        // it is an unorderable add offered in the most prominent place on the page. The fixture
        // marks the COMPONENT as the featured one, so this fails without the filter.
        var featured = await FetchFeaturedAsync();

        featured.Should().BeNull("the only IsFeaturedSpecial row in the seed is a component");
    }

    // ---- the one place a component is still served -----------------------------------------------

    [Fact]
    public async Task A_component_is_still_readable_by_id_so_the_admin_editor_can_open_it()
    {
        var response = await GetFromJsonAsync<ApiResponse<ProductDto>>($"/api/Products/{MeatId}");

        response!.Data.Should().NotBeNull();
        response.Data!.Name.Should().Be(MeatName);
        response.Data.IsComponent.Should().BeTrue("the editor has to render the checkbox as ticked");
    }

    [Fact]
    public async Task The_summary_dto_carries_the_flag_so_an_admin_list_can_label_the_row()
    {
        var page = await FetchProductsAsync(includeComponents: true);

        page.Items.Single(p => p.Name == MeatName).IsComponent.Should().BeTrue();
        page.Items.Single(p => p.Name == PlainName).IsComponent.Should().BeFalse();
    }

    // ---- the write path ---------------------------------------------------------------------------

    [Fact]
    public async Task The_flag_round_trips_through_create_and_update()
    {
        AuthenticateAsAdmin();

        var categoryId = await PrimaryCategoryIdAsync();
        var created = await PostAsJsonAsync("/api/Products", new
        {
            name = "G5 Viande Creee",
            basePrice = 3.5m,
            isActive = true,
            isAvailable = true,
            isSpecial = false,
            preparationTimeMinutes = 5,
            type = (int)ProductType.MainItem,
            kitchenType = (int)KitchenType.None,
            displayOrder = 0,
            categoryIds = new[] { categoryId },
            primaryCategoryId = categoryId,
            content = new Dictionary<string, object>
            {
                ["en"] = new { name = "Created meat", description = "d" },
            },
            isComponent = true,
        });

        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdDto = (await ReadResponseAsync<ApiResponse<ProductDto>>(created))!.Data!;
        createdDto.IsComponent.Should().BeTrue("the POST body said so");

        // Reloaded from the database, not read back off the command's own echo — the echo would be
        // true even if the column were never written.
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await context.Products.AsNoTracking().FirstAsync(p => p.Id == createdDto.Id);
            stored.IsComponent.Should().BeTrue();
        }

        // …and the admin can change their mind. `false` is the DEFAULT, so this direction is the
        // one that a "never read the field, always write the default" bug would still pass; the
        // create above is what makes the pair decisive.
        var updated = await PutAsJsonAsync($"/api/Products/{createdDto.Id}", new
        {
            id = createdDto.Id,
            name = "G5 Viande Creee",
            basePrice = 3.5m,
            isActive = true,
            isAvailable = true,
            isSpecial = false,
            preparationTimeMinutes = 5,
            type = (int)ProductType.MainItem,
            kitchenType = (int)KitchenType.None,
            displayOrder = 0,
            categoryIds = new[] { categoryId },
            primaryCategoryId = categoryId,
            isComponent = false,
        });

        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadResponseAsync<ApiResponse<ProductDto>>(updated))!.Data!.IsComponent.Should().BeFalse();
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private async Task<PagedResult<ProductSummaryDto>> FetchProductsAsync(bool includeComponents)
    {
        using var scope = Factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        var response = await mediator.SendQuery<ProductsQuery, ApiResponse<PagedResult<ProductSummaryDto>>>(
            new ProductsQuery(
                CategoryId: null, Type: null, ExcludeType: null, IsActive: null, IsAvailable: null,
                isSpeacial: null, Search: null, Page: 1, PageSize: 50,
                IncludeComponents: includeComponents));
        return response.Data!;
    }

    private async Task<PagedResult<SpecialProductDto>> FetchSpecialsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        var response = await mediator.SendQuery<SpecialsQuery, ApiResponse<PagedResult<SpecialProductDto>>>(
            new SpecialsQuery(Page: 1, PageSize: 50));
        return response.Data!;
    }

    private async Task<FeaturedSpecialDto?> FetchFeaturedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        var response = await mediator.SendQuery<FeaturedQuery, ApiResponse<FeaturedSpecialDto?>>(
            new FeaturedQuery());
        return response.Data;
    }

    private async Task<Guid> PrimaryCategoryIdAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await context.Categories.AsNoTracking().FirstAsync(c => c.Name == "G5 Catalogue")).Id;
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var category = new Category { Name = "G5 Catalogue", AvailableOrderTypes = null, CreatedBy = Actor };

        // 4 products: 2 components + 2 ordinary. The counts asserted above are exactly these,
        // because overriding SeedTestData makes the base wipe and reseed before EVERY test in this
        // class, and the base seed contains no products.
        var meat = NewProduct(MeatId, MeatName, category, isComponent: true);
        var plain = NewProduct(PlainId, PlainName, category, isComponent: false);

        // The component is the FEATURED one on purpose: seed an ordinary product as featured
        // instead and the banner test passes with the filter reverted, measuring a bystander.
        var componentSpecial = NewProduct(Guid.NewGuid(), ComponentSpecialName, category, isComponent: true);
        componentSpecial.IsSpecial = true;
        componentSpecial.IsFeaturedSpecial = true;
        componentSpecial.FeaturedDate = DateTime.UtcNow;

        var plainSpecial = NewProduct(Guid.NewGuid(), PlainSpecialName, category, isComponent: false);
        plainSpecial.IsSpecial = true;

        context.AddRange(meat, plain, componentSpecial, plainSpecial);
        await context.SaveChangesAsync();
    }

    private static Product NewProduct(Guid id, string name, Category category, bool isComponent)
    {
        var product = new Product
        {
            Id = id,
            Name = name,
            BasePrice = 4m,
            // ACTIVE and AVAILABLE on purpose: seed a component inactive and every exclusion below
            // passes for the WRONG reason, since IsActive already filters the specials surfaces.
            IsActive = true,
            IsAvailable = true,
            IsComponent = isComponent,
            Type = ProductType.MainItem,
            AvailableOrderTypes = null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor,
        };
        product.ProductCategories.Add(new ProductCategory { Category = category, IsPrimary = true, CreatedBy = Actor });
        return product;
    }
}
