using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using ProductsQuery = RestaurantSystem.Api.Features.Products.Queries.GetProductsQuery.GetProductsQuery;

namespace RestaurantSystem.IntegrationTests.Features.Categories;

/// <summary>
/// Partner request 2026-09-06 (MC FOOD): a category the owner marks "hide from the All tab" keeps
/// its own tab and stays fully orderable — its products only leave the guest's COMBINED All list.
/// The three failure modes that split independently, so each is pinned: a filter that hides the
/// category's products everywhere (the tab goes empty), a filter that also fires for staff (the
/// admin can no longer manage what they hid), and a product shared with a visible category being
/// dragged out of All by its hidden half.
/// </summary>
[Collection("Database Lane 2")]
public class CategoryHiddenFromAllTabTests : IntegrationTestBase
{
    public CategoryHiddenFromAllTabTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private const string Actor = "hide-alltab-test";
    private const string HiddenOnly = "HT Plat Caché Seul";
    private const string Visible = "HT Plat Visible";
    private const string Mixed = "HT Plat Mixte";

    private static readonly Guid HiddenCategoryId = Guid.NewGuid();
    private static readonly Guid VisibleCategoryId = Guid.NewGuid();

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var hidden = new Category
        {
            Id = HiddenCategoryId,
            Name = "HT Cachée",
            IsHiddenFromAllTab = true,
            DisplayOrder = 1,
            AvailableOrderTypes = null,
            CreatedBy = Actor,
        };
        var visible = new Category
        {
            Id = VisibleCategoryId,
            Name = "HT Visible",
            DisplayOrder = 2,
            AvailableOrderTypes = null,
            CreatedBy = Actor,
        };

        var hiddenOnly = NewProduct(Guid.NewGuid(), HiddenOnly, displayOrder: 0);
        hiddenOnly.ProductCategories.Add(new ProductCategory { Category = hidden, IsPrimary = true, CreatedBy = Actor });

        // Positive control for every exclusion below: an ordinary product that must never move.
        var visibleProduct = NewProduct(Guid.NewGuid(), Visible, displayOrder: 100);
        visibleProduct.ProductCategories.Add(new ProductCategory { Category = visible, IsPrimary = true, CreatedBy = Actor });

        // A dish ALSO carried by a visible category stays in All ("any visible category shows it")
        // — hiding one of its two categories must not erase the dish from the menu.
        var mixed = NewProduct(Guid.NewGuid(), Mixed, displayOrder: 200);
        mixed.ProductCategories.Add(new ProductCategory { Category = visible, IsPrimary = true, CreatedBy = Actor });
        mixed.ProductCategories.Add(new ProductCategory { Category = hidden, IsPrimary = false, CreatedBy = Actor });

        context.AddRange(hidden, visible, hiddenOnly, visibleProduct, mixed);
        await context.SaveChangesAsync();
    }

    private static Product NewProduct(Guid id, string name, int displayOrder)
    {
        return new Product
        {
            Id = id,
            Name = name,
            BasePrice = 4m,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.MainItem,
            DisplayOrder = displayOrder,
            AvailableOrderTypes = null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor,
        };
    }

    private async Task<List<string>> FetchGuestAllViewNamesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        var response = await mediator.SendQuery<ProductsQuery, ApiResponse<PagedResult<ProductSummaryDto>>>(
            new ProductsQuery(
                CategoryId: null, Type: null, ExcludeType: null, IsActive: null, IsAvailable: null,
                isSpeacial: null, Search: null, Page: 1, PageSize: 200,
                IncludeComponents: false));
        return response.Data!.Items.Select(p => p.Name).ToList();
    }

    [Fact]
    public async Task The_guest_all_view_leaves_out_a_hidden_category_products()
    {
        var names = await FetchGuestAllViewNamesAsync();

        names.Should().NotContain(HiddenOnly, "its only category is hidden from the All tab");
        names.Should().Contain(Visible, "positive control: visible categories are untouched");
    }

    [Fact]
    public async Task A_dish_also_carried_by_a_visible_category_stays_in_the_all_view()
    {
        var names = await FetchGuestAllViewNamesAsync();

        names.Should().Contain(Mixed, "one visible category is enough for the dish to stay listed");
    }

    [Fact]
    public async Task The_hidden_category_own_tab_still_lists_its_products()
    {
        using var scope = Factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        var response = await mediator.SendQuery<ProductsQuery, ApiResponse<PagedResult<ProductSummaryDto>>>(
            new ProductsQuery(
                CategoryId: HiddenCategoryId, Type: null, ExcludeType: null, IsActive: null, IsAvailable: null,
                isSpeacial: null, Search: null, Page: 1, PageSize: 200,
                IncludeComponents: false));

        response.Data!.Items.Should().Contain(p => p.Name == HiddenOnly,
            "\"hide from the All tab\" means exactly that — the category tab stays a full menu");
    }

    [Fact]
    public async Task Staff_still_see_hidden_category_products_in_the_all_view()
    {
        AuthenticateAsAdmin();
        var page = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>(
            "/api/Products?Page=1&PageSize=200");

        page!.Data!.Items.Should().Contain(p => p.Name == HiddenOnly,
            "the admin must still see every category's products to manage the flag itself");
        page.Data.Items.Should().Contain(p => p.Name == Visible, "positive control");
    }
}
