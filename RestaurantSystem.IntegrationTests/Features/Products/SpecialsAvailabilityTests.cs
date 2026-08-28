using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using FeaturedQuery = RestaurantSystem.Api.Features.Products.Queries.GetFeaturedSpecialQuery.GetFeaturedSpecialQuery;
using SpecialsQuery = RestaurantSystem.Api.Features.Products.Queries.GetSpecialProductsQuery.GetSpecialProductsQuery;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// G7 / §9.2 — the specials surfaces had no <c>availability</c> at all, so the featured banner and
/// the specials carousel rendered a restricted item as fully orderable. The banner is an ENTRY
/// POINT: a guest can order straight from it, so this was an unguarded add, not a cosmetic gap.
/// <para>
/// These drive the real handlers through the mediator, and the seeded product INHERITS its channels
/// from its primary category. That combination is the point: <c>OrderTypeAvailability</c> resolves
/// inheritance through <c>ProductCategories -&gt; Category</c>, and an unloaded collection reads as
/// UNRESTRICTED rather than throwing — so a missing include produces a permissive verdict silently.
/// Asserting on the resolver alone would pass with the includes still absent.
/// </para>
/// </summary>
[Collection("Database Lane 1")]
public class SpecialsAvailabilityTests : IntegrationTestBase
{
    public SpecialsAvailabilityTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const string InheritingSpecialName = "G7 Inheriting Special";
    /// <summary>A special the restaurant has switched OFF — <c>IsSpecial</c> true, <c>IsActive</c> false.</summary>
    private const string DeactivatedSpecialName = "Deactivated Special";
    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);

    [Fact]
    public async Task FeaturedSpecial_OnARefusedChannel_ReportsBlockedWithTheChannelsItDoesAllow()
    {
        var featured = await FetchFeaturedAsync(OrderType.DineIn);

        featured.Should().NotBeNull();
        // Pre-fix there was no field to assert at all; with the field but no include this is `true`.
        featured!.Availability.CanOrder.Should().BeFalse("the category this special inherits from refuses dine-in");
        featured.Availability.Reason.Should().Be(AvailabilityReason.WrongOrderType);
        featured.Availability.AllowedOrderTypes.Should().BeEquivalentTo(new[] { OrderType.Takeaway, OrderType.Delivery });
        featured.Availability.InheritsOrderTypes.Should().BeTrue("the product carries no mask of its own");
    }

    [Fact]
    public async Task FeaturedSpecial_OnAnAllowedChannel_IsOrderable()
    {
        var featured = await FetchFeaturedAsync(OrderType.Takeaway);

        featured!.Availability.CanOrder.Should().BeTrue();
        featured.Availability.Reason.Should().Be(AvailabilityReason.Available);
    }

    [Fact]
    public async Task FeaturedSpecial_WithNoChannelChosen_IsOrderableButStillNamesItsRestriction()
    {
        // The dominant browse state: nothing is blocked, but the chip still has to say where the
        // item CAN be ordered, which is what `AllowedOrderTypes` is for.
        var featured = await FetchFeaturedAsync(requestedOrderType: null);

        featured!.Availability.CanOrder.Should().BeTrue();
        featured.Availability.AllowedOrderTypes.Should().BeEquivalentTo(new[] { OrderType.Takeaway, OrderType.Delivery });
    }

    [Fact]
    public async Task SpecialsList_CarriesTheSameVerdictAsTheBanner()
    {
        var specials = await FetchSpecialsAsync(OrderType.DineIn);
        var special = specials.Items.Single(p => p.Name == InheritingSpecialName);

        special.Availability.CanOrder.Should().BeFalse();
        special.Availability.AllowedOrderTypes.Should().BeEquivalentTo(new[] { OrderType.Takeaway, OrderType.Delivery });
    }

    /// <summary>
    /// Everything above drives the handler through the mediator, which cannot see the HTTP binding.
    /// A renamed or dropped <c>[FromQuery]</c> parameter would leave those green while the endpoint
    /// silently ignored the channel — the exact "the guard was never armed" shape §9.13 describes,
    /// where nothing throws and the feature is simply inert. The wire name matters: the frontend
    /// sends PascalCase <c>RequestedOrderType</c>.
    /// </summary>
    [Fact]
    public async Task FeaturedSpecial_BindsTheChannelFromTheQueryString_NotJustTheHandler()
    {
        var blocked = await GetFromJsonAsync<ApiResponse<FeaturedSpecialDto?>>(
            "/api/Products/featured-special?RequestedOrderType=DineIn");
        var allowed = await GetFromJsonAsync<ApiResponse<FeaturedSpecialDto?>>(
            "/api/Products/featured-special?RequestedOrderType=Takeaway");

        blocked!.Data!.Availability.CanOrder.Should().BeFalse("the query string asked about dine-in");
        allowed!.Data!.Availability.CanOrder.Should().BeTrue();
    }

    [Fact]
    public async Task SpecialsList_BindsTheChannelFromTheQueryString()
    {
        var response = await GetFromJsonAsync<ApiResponse<PagedResult<SpecialProductDto>>>(
            "/api/Products/specials?RequestedOrderType=DineIn");

        var special = response!.Data!.Items.Single(p => p.Name == InheritingSpecialName);
        special.Availability.CanOrder.Should().BeFalse();
    }

    /// <summary>
    /// The two specials queries answer one question and disagreed about a DEACTIVATED special:
    /// <c>GetFeaturedSpecialQuery</c> filtered <c>IsActive</c>, <c>GetSpecialProductsQuery</c> did
    /// not — on an <c>[AllowAnonymous]</c> endpoint. Both halves are asserted together, because the
    /// point is that they now AGREE; asserting the list alone would pass again the moment the
    /// featured query drifted the other way.
    /// <para>
    /// ⚠️ THE FIXTURE IS LOAD-BEARING, and it is the easiest thing here to "simplify" into
    /// uselessness. <see cref="DeactivatedSpecialName"/> is seeded <c>IsAvailable = TRUE</c> ON
    /// PURPOSE. Seed it unavailable and BOTH surfaces filter it for the WRONG reason — this test
    /// then passes with the fix reverted, measuring a bystander instead of the hypothesis. That is
    /// the same vacuity as the first draft of
    /// <see cref="Specials_TotalCount_DoesNotCountTheDeactivatedItem"/> in a different costume.
    /// Do not "tidy" the seed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Specials_ExcludeADeactivatedItem_AndTheTwoQueriesAgree()
    {
        var specials = await FetchSpecialsAsync(requestedOrderType: null);

        specials.Items.Should().NotContain(
            p => p.Name == DeactivatedSpecialName,
            "an item the restaurant switched off is not a special anyone may see");
        // The row is gone, so the `Set Featured` action goes with it — and that action was a silent
        // no-op on this item, since the banner refuses an inactive product.
        specials.Items.Should().Contain(p => p.Name == InheritingSpecialName, "the ACTIVE special is untouched");

        var featured = await FetchFeaturedAsync(requestedOrderType: null);
        featured!.Name.Should().NotBe(DeactivatedSpecialName);
    }

    /// <summary>
    /// The count is part of the contract: <c>TotalCount</c> is computed from the same filtered
    /// query, so a paginator built on it cannot advertise a page of rows the list will not serve.
    /// <para>
    /// The ABSOLUTE count is asserted, not <c>TotalCount == Items.Count</c>. The first draft of this
    /// test did the latter and was VACUOUS — both sides move together, so it passed with the fix
    /// reverted and proved only that one page was not truncated. Two is the pre-fix number and one
    /// is the post-fix number, so this assertion can actually fail.
    /// </para>
    /// <para>
    /// The number is deterministic: this class overrides <c>SeedTestData</c>, which makes
    /// <c>IntegrationTestBase</c> wipe and reseed before EVERY test in it, and the base seed
    /// contains no specials at all. So the specials universe here is exactly the two this class
    /// creates — one active, one deactivated.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Specials_TotalCount_DoesNotCountTheDeactivatedItem()
    {
        var specials = await FetchSpecialsAsync(requestedOrderType: null);

        specials.TotalCount.Should().Be(
            1, "the seed holds two specials and one of them is switched off; pre-fix this was 2");
        specials.Items.Count.Should().Be(specials.TotalCount, "the page and the count come from one filtered query");
    }

    private async Task<FeaturedSpecialDto?> FetchFeaturedAsync(OrderType? requestedOrderType)
    {
        using var scope = Factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        var response = await mediator.SendQuery<FeaturedQuery, ApiResponse<FeaturedSpecialDto?>>(
            new FeaturedQuery(requestedOrderType));
        return response.Data;
    }

    private async Task<PagedResult<SpecialProductDto>> FetchSpecialsAsync(OrderType? requestedOrderType)
    {
        using var scope = Factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        var response = await mediator.SendQuery<SpecialsQuery, ApiResponse<PagedResult<SpecialProductDto>>>(
            new SpecialsQuery(Page: 1, PageSize: 20, RequestedOrderType: requestedOrderType));
        return response.Data!;
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var category = new Category
        {
            Name = "G7 Takeaway-Only Category",
            AvailableOrderTypes = TakeawayAndDelivery,
            CreatedBy = "test"
        };

        var product = new Product
        {
            Name = InheritingSpecialName,
            BasePrice = 21.00m,
            IsActive = true,
            IsAvailable = true,
            IsSpecial = true,
            IsFeaturedSpecial = true,
            FeaturedDate = DateTime.UtcNow,
            // No mask of its own: the verdict has to come through the category, which is the whole
            // reason the include matters.
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

        // The item this fix is about: marked special, then switched OFF. It must not reach either
        // specials surface. IsAvailable stays TRUE deliberately — the admin table's Status column
        // and its `Set Featured` gate both read IsAvailable, so an item that is unavailable would
        // be filtered out for the WRONG reason and the test would pass without the fix.
        var deactivated = new Product
        {
            Name = DeactivatedSpecialName,
            BasePrice = 14.00m,
            IsActive = false,
            IsAvailable = true,
            IsSpecial = true,
            IsFeaturedSpecial = false,
            AvailableOrderTypes = null,
            CreatedBy = "test"
        };
        deactivated.ProductCategories.Add(new ProductCategory
        {
            Category = category,
            IsPrimary = true,
            CreatedBy = "test"
        });

        context.Add(deactivated);
        await context.SaveChangesAsync();
    }
}
