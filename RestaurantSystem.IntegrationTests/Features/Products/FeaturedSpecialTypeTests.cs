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

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// The featured banner had no way to say what KIND of product it was showing.
/// <para>
/// A combo is not its own entity — it is a <see cref="ProductType.Menu"/> product owning a
/// <c>MenuDefinition</c> — and <c>SetFeaturedSpecialCommand</c> checks only <c>IsSpecial</c> and
/// <c>IsActive</c>, so a combo CAN be the featured item. <see cref="FeaturedSpecialDto"/> carried
/// neither <c>Type</c> nor <c>MenuDefinition</c>, so a client rendering an admin inline base-price
/// edit on the banner could only guess, and would dispatch a combo to the product price endpoint.
/// </para>
/// <para>
/// The menu-type case is the one that matters: <see cref="ProductType.MainItem"/> is <c>0</c>, so a
/// handler that never assigns <c>Type</c> reports <c>mainItem</c> for everything and a test seeded
/// with a plain product would pass with the mapping deleted.
/// </para>
/// </summary>
[Collection("Database Lane 4")]
public class FeaturedSpecialTypeTests : IntegrationTestBase
{
    public FeaturedSpecialTypeTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const string FeaturedComboName = "Featured Combo";

    [Fact]
    public async Task FeaturedSpecial_WhenTheFeaturedItemIsACombo_ReportsTheMenuType()
    {
        var featured = await FetchFeaturedAsync();

        featured.Should().NotBeNull();
        featured!.Name.Should().Be(FeaturedComboName);
        featured.Type.Should().Be(
            ProductType.Menu,
            "a client cannot tell a combo from a plain product without it, and the two take different write paths");
    }

    /// <summary>
    /// The handler runs behind the mediator above, which cannot see the JSON contract. The wire
    /// NAME and VALUE both matter: the frontend reads <c>type</c> and compares it to the string
    /// <c>"menu"</c> (the <c>EnumMember</c> value), not to the ordinal.
    /// </summary>
    [Fact]
    public async Task FeaturedSpecial_SerializesTheTypeAsItsEnumMemberString()
    {
        var raw = await Client.GetStringAsync("/api/Products/featured-special");

        raw.Should().Contain("\"type\":\"menu\"");
    }

    private async Task<FeaturedSpecialDto?> FetchFeaturedAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        var response = await mediator.SendQuery<FeaturedQuery, ApiResponse<FeaturedSpecialDto?>>(
            new FeaturedQuery(null));
        return response.Data;
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Add(new Product
        {
            Name = FeaturedComboName,
            BasePrice = 34.00m,
            Type = ProductType.Menu,
            IsActive = true,
            IsAvailable = true,
            IsSpecial = true,
            IsFeaturedSpecial = true,
            FeaturedDate = DateTime.UtcNow,
            CreatedBy = "test"
        });

        await context.SaveChangesAsync();
    }
}
