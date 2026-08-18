using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Catalog;

/// <summary>
/// §9.14 — one data state, two verdicts. <c>Category</c> is soft-deleted behind a global query
/// filter; <c>ProductCategory</c> is not. So when a product's PRIMARY category is soft-deleted,
/// <c>GetProductsQuery</c> DROPS the join row along with its category and answers permissively,
/// while <c>GetProductByIdQuery</c> — whose <c>IgnoreQueryFilters()</c> un-filters the includes —
/// resolves THROUGH the deleted category and blocks. Measured in both directions before the fix.
/// §9.10's disagreement shape with the surfaces swapped: the card said yes, the sheet said no.
/// <para>
/// <b>How the data state is reached.</b> Not by the ordinary admin flow: <c>DeleteCategoryCommand</c>
/// refuses to delete a category that still has live products, and both product write paths validate
/// <c>CategoryIds</c> against the FILTERED category set, so neither end can create it alone. What
/// remains is a delete-vs-assign race across two transactions (the same TOCTOU shape §9.13 hit), a
/// category deleted while its only products were soft-deleted, and out-of-band database writes —
/// which is also why this test seeds the flag directly rather than calling the command. Rare, then,
/// but the two-answer bug it produces is silent and permanent.
/// </para>
/// <para>
/// The fix makes the shared resolver refuse to inherit from a deleted category, so the answer no
/// longer depends on which query filters ran. PERMISSIVE is the right side to land on: a restriction
/// inherited from a category the admin can no longer see or edit is an invisible block on sales, and
/// "no primary category" already resolves permissively by documented design.
/// </para>
/// </summary>
[Collection("Database Lane 2")]
public class SoftDeletedCategoryAvailabilityTests : IntegrationTestBase
{
    private const string ProductName = "§9.14 Orphaned-Primary Product";
    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);

    private Guid _productId;

    public SoftDeletedCategoryAvailabilityTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>
    /// The list was ALREADY permissive — this is the side the fix keeps, pinned so the agreement is
    /// two-sided rather than "whatever the detail query happens to do".
    /// </summary>
    [Fact]
    public async Task ProductList_DoesNotInheritFromADeletedCategory()
    {
        var summary = await FetchListedAsync(OrderType.DineIn);

        summary.Availability.CanOrder.Should().BeTrue(
            "a category the admin can no longer see must not keep blocking sales");
        summary.Availability.AllowedOrderTypes.Should().HaveCount(3);
        summary.Availability.InheritsOrderTypes.Should().BeTrue("the product still carries no mask of its own");
    }

    [Fact]
    public async Task ProductList_ReportsNoCategoriesAtAllForADeletedOne()
    {
        var summary = await FetchListedAsync(OrderType.DineIn);

        summary.CategoryNames.Should().BeEmpty("the join row is filtered out with its category");
        summary.PrimaryCategoryName.Should().BeNull();
    }

    /// <summary>
    /// Pins the EF behaviour the rest of this class reasons from, at the level where it is actually
    /// observable: on a query whose filters DO run, the join ROW is dropped rather than handed back
    /// with a null <c>Category</c>. Asserting it through the endpoint would not do — the new filter
    /// makes both behaviours look identical from there — and it matters because it is the reason the
    /// pre-fix list dereferenced <c>pc.Category.Name</c> and still returned 200 instead of 500,
    /// i.e. the reason the two surfaces disagreed SILENTLY for as long as they did.
    /// </summary>
    [Fact]
    public async Task EfDropsTheJoinRowWithItsCategory_NotJustTheNavigation()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var product = await context.Products
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .FirstAsync(p => p.Id == _productId);

        product.ProductCategories.Should().BeEmpty();
    }

    /// <summary>
    /// The §9.14 gap itself. Structural equivalence, so a future divergence on EITHER surface fails
    /// here rather than being re-baselined twice.
    /// </summary>
    [Fact]
    public async Task ProductDetail_AgreesWithTheList()
    {
        var listed = await FetchListedAsync(OrderType.DineIn);
        var detail = await GetFromJsonAsync<ApiResponse<ProductDto>>(
            $"/api/Products/{_productId}?RequestedOrderType=DineIn");

        detail!.Data!.Availability.Should().BeEquivalentTo(listed.Availability,
            "the detail query ignores query filters, so it used to resolve THROUGH the deleted category");
        detail.Data.Availability.CanOrder.Should().BeTrue(
            "equivalence alone would also pass if BOTH surfaces blocked it");
    }

    /// <summary>
    /// The same one-state-two-answers problem for the category NAMES, not just the verdict: the
    /// editor listed a category the admin can no longer pick, and its own list endpoint did not.
    /// </summary>
    [Fact]
    public async Task ProductDetail_DoesNotListTheDeletedCategoryAsAnAssignment()
    {
        var detail = await GetFromJsonAsync<ApiResponse<ProductDto>>($"/api/Products/{_productId}");

        detail!.Data!.Categories.Should().BeEmpty();
        detail.Data.PrimaryCategory.Should().BeNull();
    }

    /// <summary>
    /// The control: a LIVE category still restricts. Without this, deleting the whole inheritance
    /// branch would satisfy every assertion above.
    /// </summary>
    [Fact]
    public async Task ALiveCategoryStillRestricts()
    {
        await ReviveTheCategoryAsync();

        var listed = await FetchListedAsync(OrderType.DineIn);
        var detail = await GetFromJsonAsync<ApiResponse<ProductDto>>(
            $"/api/Products/{_productId}?RequestedOrderType=DineIn");

        listed.Availability.CanOrder.Should().BeFalse();
        detail!.Data!.Availability.CanOrder.Should().BeFalse();
        listed.PrimaryCategoryName.Should().NotBeNull();
        detail.Data.PrimaryCategory.Should().NotBeNull();
    }

    private async Task<ProductSummaryDto> FetchListedAsync(OrderType requestedOrderType)
    {
        var response = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>(
            $"/api/Products?page=1&pageSize=100&RequestedOrderType={requestedOrderType}");
        return response!.Data!.Items.Single(p => p.Name == ProductName);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var category = new Category
        {
            Name = "§9.14 Deleted Takeaway-Only Category",
            AvailableOrderTypes = TakeawayAndDelivery,
            CreatedBy = "test"
        };

        var product = new Product
        {
            Name = ProductName,
            BasePrice = 12.00m,
            IsActive = true,
            IsAvailable = true,
            // Inherits: the verdict can only come through the category, which is the point.
            AvailableOrderTypes = null,
            CreatedBy = "test"
        };
        product.ProductCategories.Add(new ProductCategory
        {
            Category = category,
            IsPrimary = true,
            CreatedBy = "test"
        });

        context.Add(product);
        await context.SaveChangesAsync();
        _productId = product.Id;

        // Soft-delete the category exactly as `DeleteCategoryCommand` does — by setting the flag,
        // NOT via `Remove()`: `ApplicationDbContext` only overrides the SYNCHRONOUS `SaveChanges`, so
        // `Remove()` + `SaveChangesAsync` HARD-deletes and would cascade the join row away, leaving a
        // product with no categories at all. That state is permissive for a different reason and
        // would pass every assertion here without the fix. The ProductCategory join row is not
        // soft-deletable and is deliberately left pointing at the deleted category: that is the
        // reachable data state §9.14 is about.
        category.IsDeleted = true;
        category.DeletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    private async Task ReviveTheCategoryAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = await context.Categories
            // soft-delete-bypass: the test needs the deleted row back to prove a LIVE category still
            // restricts, which is the control for every assertion above.
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Name == "§9.14 Deleted Takeaway-Only Category");
        category.IsDeleted = false;
        category.DeletedAt = null;
        await context.SaveChangesAsync();
    }
}
