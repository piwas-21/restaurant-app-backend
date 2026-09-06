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
using ProductsQuery = RestaurantSystem.Api.Features.Products.Queries.GetProductsQuery.GetProductsQuery;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// Partner feedback 2026-09-06 (MC FOOD): the guest's ALL view used to order the catalogue flat by
/// <c>Product.DisplayOrder</c>, so items interleaved across categories. The All view now follows the
/// menu's own structure: primary category <c>DisplayOrder</c> first, the product's order within it
/// second — and staff keeps the flat order, because the admin list edits DisplayOrder against it.
/// </summary>
/// <remarks>
/// Order assertions are RELATIVE (IndexOf comparisons), never whole-sequence equality: the base
/// seed contributes products of its own to the guest listing, and the assertion must survive them.
/// </remarks>
[Collection("Database Lane 4")]
public class AllTabCategoryOrderTests : IntegrationTestBase
{
    public AllTabCategoryOrderTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private const string Actor = "alltab-order-test";
    private const string EarlyOnly = "Ord Tab Early Only";
    private const string BothPrimaryEarly = "Ord Tab Early Primary";
    private const string LateOnly = "Ord Tab Late Only";

    private static readonly Guid EarlyId = Guid.NewGuid();
    private static readonly Guid LateId = Guid.NewGuid();

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Creation order is the REVERSE of display order on purpose: a pass that sorts by id or
        // insertion would look green here and fail in production.
        var late = new Category { Name = "Ord Tab Late", DisplayOrder = 2, AvailableOrderTypes = null, CreatedBy = Actor };
        var early = new Category { Name = "Ord Tab Early", DisplayOrder = 1, AvailableOrderTypes = null, CreatedBy = Actor };

        // The two orders must genuinely DIVERGE, or the assertions cannot discriminate: LateOnly
        // carries the BEST flat DisplayOrder (0) yet lives in the LAST category, EarlyOnly a worse
        // one (100) in the first. Post-fix: EarlyOnly, BothPrimaryEarly, LateOnly.
        // Pre-fix flat sort: LateOnly(0), EarlyOnly(100), BothPrimaryEarly(200) — the exact
        // reverse, so this test FAILS against the old ordering instead of passing vacuously.
        var earlyOnly = NewProduct(Guid.NewGuid(), EarlyOnly, displayOrder: 100);
        earlyOnly.ProductCategories.Add(new ProductCategory { Category = early, IsPrimary = true, CreatedBy = Actor });

        var lateOnly = NewProduct(Guid.NewGuid(), LateOnly, displayOrder: 0);
        lateOnly.ProductCategories.Add(new ProductCategory { Category = late, IsPrimary = true, CreatedBy = Actor });

        // A product in BOTH categories sits at its PRIMARY category's position — the decided
        // semantic ("any visible category shows it in All" is the filter; the primary owns the sort).
        var both = NewProduct(Guid.NewGuid(), BothPrimaryEarly, displayOrder: 200);
        both.ProductCategories.Add(new ProductCategory { Category = early, IsPrimary = true, CreatedBy = Actor });
        both.ProductCategories.Add(new ProductCategory { Category = late, IsPrimary = false, CreatedBy = Actor });

        context.AddRange(early, late, earlyOnly, lateOnly, both);
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
    public async Task The_guest_all_view_groups_items_by_primary_category_display_order()
    {
        var names = await FetchGuestAllViewNamesAsync();

        names.Should().Contain(EarlyOnly, "positive control: the seeded items must be listed at all");
        names.Should().Contain(LateOnly);
        names.Should().Contain(BothPrimaryEarly);

        // Category 1's members — both of them, in their own DisplayOrder — come before category
        // 2's, whose product DisplayOrder (0) is the smallest in the whole seed: under the old
        // flat sort that smallest order put LateOnly FIRST, so both assertions below fail there.
        names.IndexOf(LateOnly).Should().BeGreaterThan(
            names.IndexOf(EarlyOnly), "category 2 comes after category 1 despite the best flat DisplayOrder");
        names.IndexOf(LateOnly).Should().BeGreaterThan(
            names.IndexOf(BothPrimaryEarly), "category 2 comes after category 1");
        names.IndexOf(BothPrimaryEarly).Should().BeGreaterThan(
            names.IndexOf(EarlyOnly), "within one category, the product's own DisplayOrder still rules");
    }
}
