using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
        var category = Category("Literal search category");
        var eligible = Product(category, "Candidate 100%_ literal", 5.25m);
        var wrong = Product(category, "Candidate 100XX literal", 9m);
        var unsupported = Product(category, "Candidate 100%_ blocked", 7m);
        unsupported.Allergens = ["peanuts"];
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Categories.Add(category);
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
        var category = Category("Variant candidate category");
        var product = Product(category, "Candidate variant meal", 5.25m);
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
            context.Categories.Add(category); context.Products.Add(product); await context.SaveChangesAsync();
        }
        var result = await Read(new("extra large"));
        var item = result.Items.Should().ContainSingle().Subject;
        item.ProductId.Should().Be(product.Id); item.VariationId.Should().Be(variation.Id);
        item.VariationName.Should().Be("Extra large fixture"); item.PriceMinor.Should().Be(675);
        item.Supported.Should().BeTrue();
        var categoryRows = (await Read(new("variant meal"))).Items;
        categoryRows.Should().ContainSingle(row => row.ProductId == product.Id && row.VariationId == variation.Id);
        categoryRows.Should().NotContain(row => row.ProductId == product.Id && row.VariationId == null);
    }

    [Fact]
    public async Task FlattenedPagesAreBoundedAndDoNotRepeatIdentities()
    {
        var category = Category("Paged candidate category");
        var product = Product(category, "Candidate paged meal", 5m);
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
            context.Categories.Add(category); context.Products.Add(product); await context.SaveChangesAsync();
        }
        var first = await Read(new("paged meal"));
        first.Items.Should().HaveCount(50); first.NextCursor.Should().NotBeNull();
        var second = await Read(new("paged meal", first.NextCursor!));
        second.Items.Should().HaveCount(11); second.NextCursor.Should().BeNull();
        first.Items.Concat(second.Items).Select(row => (row.ProductId, row.VariationId)).Distinct().Should().HaveCount(61);
    }

    [Fact]
    public async Task CandidateCursorFromOlderSourceReturnsRevisionConflict()
    {
        var category = Category("Stale candidate category");
        var product = Product(category, "Candidate stale page meal", 5m);
        for (var index = 0; index < 55; index++)
            product.Variations.Add(new()
            {
                Id = Guid.NewGuid(),
                Name = $"Stale variation {index}",
                CreatedBy = "test",
                Descriptions = [new() { LanguageCode = "en", Name = $"Stale variation {index}", CreatedBy = "test" }]
            });
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Categories.Add(category);
            context.Products.Add(product);
            await context.SaveChangesAsync();
        }

        var first = await Read(new("stale page meal"));
        first.NextCursor.Should().NotBeNull();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var changed = await context.Products.SingleAsync(row => row.Id == product.Id);
            changed.BasePrice = 6m;
            await context.SaveChangesAsync();
        }

        var act = () => Read(new("stale page meal", first.NextCursor!, SourceRevision: first.SourceRevision));
        var error = await act.Should().ThrowAsync<ConflictException>();
        error.Which.ErrorCode.Should().Be("SourceRevisionChanged");
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

    private static Category Category(string name) => new() { Id = Guid.NewGuid(), Name = name, CreatedBy = "test" };

    private static Product Product(Category category, string name, decimal price)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            BasePrice = price,
            CreatedBy = "test",
            Allergens = [],
            Descriptions = [new() { Name = name, Description = "Fixture description", Lang = "en", CreatedBy = "test" }]
        };
        product.ProductCategories.Add(new() { Category = category, CategoryId = category.Id, IsPrimary = true, CreatedBy = "test" });
        return product;
    }
}
