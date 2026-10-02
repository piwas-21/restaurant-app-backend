using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelCatalogueSnapshotTests(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    private const string CatalogueEndpoint = "/api/delivery-channels/catalogue/snapshot";

    private async Task<ChannelCatalogueRequest> PrepareCatalogue()
    {
        var order = await PrepareAsync();
        await using var context = DatabaseFixture.CreateContext();
        var product = await context.Products.Include(row => row.Descriptions).SingleAsync(row => row.Id == order.Items[0].ProductId);
        context.RemoveRange(product.Descriptions);
        product.BasePrice = 5.25m; product.Allergens = []; product.IsActive = true; product.IsAvailable = true;
        product.Descriptions.Add(new() { Name = "Independent menu title", Description = "Declared product description", Lang = "en", CreatedBy = "test" });
        await context.SaveChangesAsync();
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.ChannelCatalogueRead]));
        return new() { Provider = order.Provider, StoreId = order.StoreId, Currency = "CHF", IsSandbox = true, Items = [new(product.Id, null)] };
    }

    private async Task<ChannelCatalogueSnapshot> Read(ChannelCatalogueRequest request)
    {
        var response = await PostAsJsonAsync(CatalogueEndpoint, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ChannelCatalogueSnapshot>(JsonOptions))!;
    }

    [Fact]
    public async Task ActualNamesPriceAndDescriptionAreStableUntilTheSourceChanges()
    {
        var request = await PrepareCatalogue(); var original = await Read(request);
        var item = original.Items.Should().ContainSingle().Subject;
        item.Name.Should().Be("Independent menu title"); item.Description.Should().Be("Declared product description");
        item.PriceMinor.Should().Be(525); item.Available.Should().BeTrue(); item.BlockReason.Should().BeEmpty();
        (await Read(request)).Revision.Should().Be(original.Revision);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var product = await context.Products.SingleAsync(row => row.Id == request.Items[0].ProductId);
            product.BasePrice = 6.75m; await context.SaveChangesAsync();
        }
        var changed = await Read(request); changed.Revision.Should().NotBe(original.Revision);
        changed.Items[0].PriceMinor.Should().Be(675);
    }

    [Fact]
    public async Task NamedVariationUsesItsRealIdentityAndPriceAdjustment()
    {
        var request = await PrepareCatalogue(); var variationId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Set<ProductVariation>().Add(new()
            {
                Id = variationId,
                ProductId = request.Items[0].ProductId,
                Name = "Variation scalar",
                PriceModifier = 1.50m,
                CreatedBy = "test",
                Descriptions = [new() { LanguageCode = "en", Name = "Large", Description = "Variation preparation detail", CreatedBy = "test" }]
            });
            await context.SaveChangesAsync();
        }
        var result = await Read(request with { Items = [new(request.Items[0].ProductId, variationId)] });
        var item = result.Items[0]; item.VariationId.Should().Be(variationId); item.VariationName.Should().Be("Large");
        item.Name.Should().Be("Independent menu title — Large"); item.PriceMinor.Should().Be(675); item.BlockReason.Should().BeEmpty();
        item.Description.Should().Be("Declared product description\nVariation preparation detail");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VariationCannotHideAnEmptyBaseTitleOrExceedTheOrderContract(bool emptyBase)
    {
        var request = await PrepareCatalogue(); var variationId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var product = await context.Products.Include(row => row.Descriptions).SingleAsync(row => row.Id == request.Items[0].ProductId);
            if (emptyBase) product.Descriptions.Single().Name = " ";
            context.Set<ProductVariation>().Add(new()
            {
                Id = variationId,
                ProductId = product.Id,
                Name = "Fixture variant",
                CreatedBy = "test",
                Descriptions = [new() { LanguageCode = "en", Name = emptyBase ? "Large" : new string('x', 51), CreatedBy = "test" }]
            });
            await context.SaveChangesAsync();
        }
        var item = (await Read(request with { Items = [new(request.Items[0].ProductId, variationId)] })).Items[0];
        item.BlockReason.Should().Be("InvalidText"); item.PriceMinor.Should().BeNull();
    }

    [Fact]
    public async Task VariationNameAtTheSharedOrderLimitIsPublishable()
    {
        var request = await PrepareCatalogue(); var variationId = Guid.NewGuid(); var name = new string('x', 50);
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Set<ProductVariation>().Add(new()
            {
                Id = variationId,
                ProductId = request.Items[0].ProductId,
                Name = "Fixture variant",
                CreatedBy = "test",
                Descriptions = [new() { LanguageCode = "en", Name = name, CreatedBy = "test" }]
            });
            await context.SaveChangesAsync();
        }
        var item = (await Read(request with { Items = [new(request.Items[0].ProductId, variationId)] })).Items[0];
        item.BlockReason.Should().BeEmpty(); item.VariationName.Should().Be(name);
    }

    [Theory]
    [InlineData("allergens", "UnmappedAllergens")]
    [InlineData("choices", "UnsupportedChoices")]
    [InlineData("translation", "MissingTranslation")]
    [InlineData("negative-price", "InvalidPrice")]
    [InlineData("missing", "MissingProduct")]
    public async Task UnsupportedSelectedProductIsReportedWithoutSilentlyDroppingIt(string fault, string expected)
    {
        var request = await PrepareCatalogue();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var product = await context.Products.SingleAsync(row => row.Id == request.Items[0].ProductId);
            switch (fault)
            {
                case "allergens": product.Allergens = ["Unmapped allergy declaration"]; break;
                case "choices": product.SauceMin = 1; break;
                case "translation": request = request with { Language = "nl" }; break;
                case "negative-price": product.BasePrice = -1; break;
                case "missing": request = request with { Items = [new(Guid.NewGuid(), null)] }; break;
            }
            await context.SaveChangesAsync();
        }
        var item = (await Read(request)).Items.Should().ContainSingle().Subject;
        item.BlockReason.Should().Be(expected); item.PriceMinor.Should().BeNull(); item.Available.Should().BeFalse();
    }

    [Fact]
    public async Task OutOfStockProductKeepsItsCatalogueButRemainsUnavailable()
    {
        var request = await PrepareCatalogue();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var product = await context.Products.SingleAsync(row => row.Id == request.Items[0].ProductId);
            product.IsAvailable = false; await context.SaveChangesAsync();
        }
        var item = (await Read(request)).Items[0]; item.BlockReason.Should().BeEmpty();
        item.PriceMinor.Should().Be(525); item.Available.Should().BeFalse();
    }
}
