using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCandidatesQuery;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelCatalogueCandidateTests(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    [Fact]
    public async Task LiteralSearchKeepsIdentityPriceAndUnsupportedReasons()
    {
        var eligible = Product("Candidate 100%_ literal", 5.25m);
        var wrong = Product("Candidate 100XX literal", 9m);
        var unsupported = Product("Candidate 100%_ blocked", 7m);
        unsupported.Allergens = ["peanuts"];
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Products.AddRange(eligible, wrong, unsupported);
            await context.SaveChangesAsync();
        }
        var result = await Read(new("100%_"));
        result.Currency.Should().Be("CHF");
        result.Items.Select(row => row.ProductId).Should().BeEquivalentTo([eligible.Id, unsupported.Id]);
        var item = result.Items.Single(row => row.ProductId == eligible.Id);
        item.Name.Should().Be("Candidate 100%_ literal");
        item.PriceMinor.Should().Be(525); item.Supported.Should().BeTrue(); item.Available.Should().BeTrue();
        var blocked = result.Items.Single(row => row.ProductId == unsupported.Id);
        blocked.Name.Should().Be("Candidate 100%_ blocked");
        blocked.BlockReason.Should().Be("UnmappedAllergens"); blocked.Supported.Should().BeFalse();
        blocked.PriceMinor.Should().BeNull();
    }

    [Fact]
    public async Task VariationSearchReturnsItsOwnIdentityAndActualPrice()
    {
        var product = Product("Candidate variant meal", 5.25m);
        product.HideBaseProduct = true;
        var variation = new ProductVariation
        {
            Id = Guid.NewGuid(),
            Name = "Scalar variation",
            PriceModifier = 1.50m,
            CreatedBy = "test",
            Descriptions = [new() { LanguageCode = "en", Name = "Extra large fixture", CreatedBy = "test" }]
        };
        product.Variations.Add(variation);
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Products.Add(product); await context.SaveChangesAsync();
        }
        var result = await Read(new("extra large"));
        var item = result.Items.Should().ContainSingle().Subject;
        item.ProductId.Should().Be(product.Id); item.VariationId.Should().Be(variation.Id);
        item.VariationName.Should().Be("Extra large fixture"); item.PriceMinor.Should().Be(675);
        item.Supported.Should().BeTrue();
        var baseItem = (await Read(new("variant meal"))).Items.Single(row => row.VariationId is null);
        baseItem.Supported.Should().BeFalse(); baseItem.BlockReason.Should().Be("VariationRequired");
    }

    [Fact]
    public async Task FlattenedPagesAreBoundedAndDoNotRepeatIdentities()
    {
        var product = Product("Candidate paged meal", 5m);
        for (var index = 0; index < 60; index++)
            product.Variations.Add(new()
            {
                Id = Guid.NewGuid(),
                Name = $"Variation {index}",
                CreatedBy = "test",
                Descriptions = [new() { LanguageCode = "en", Name = $"Variation {index}", CreatedBy = "test" }]
            });
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Products.Add(product); await context.SaveChangesAsync();
        }
        var first = await Read(new("paged meal"));
        first.Items.Should().HaveCount(50); first.NextCursor.Should().NotBeNull();
        var second = await Read(new("paged meal", first.NextCursor!));
        second.Items.Should().HaveCount(11); second.NextCursor.Should().BeNull();
        first.Items.Concat(second.Items).Select(row => (row.ProductId, row.VariationId)).Distinct().Should().HaveCount(61);
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("MQ")]
    [InlineData("LTE")]
    public async Task InvalidCursorIsRefused(string cursor)
    {
        var act = () => Read(new(Cursor: cursor));
        await act.Should().ThrowAsync<BadRequestException>();
    }

    private async Task<ChannelCatalogueCandidatesDto> Read(GetChannelCatalogueCandidatesQuery query)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CustomMediator>().SendQuery(query);
    }

    private static Product Product(string name, decimal price) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        BasePrice = price,
        CreatedBy = "test",
        Allergens = [],
        Descriptions = [new() { Name = name, Description = "Fixture description", Lang = "en", CreatedBy = "test" }]
    };
}
