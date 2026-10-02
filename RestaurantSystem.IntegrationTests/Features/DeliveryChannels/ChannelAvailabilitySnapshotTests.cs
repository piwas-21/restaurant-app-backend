using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelAvailabilitySnapshotTests(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    private const string AvailabilityEndpoint = "/api/delivery-channels/catalogue/availability";

    [Theory]
    [InlineData("available", true, "Available")]
    [InlineData("stock", false, "UnavailableProduct")]
    [InlineData("inactive", false, "UnavailableProduct")]
    [InlineData("deleted", false, "MissingProduct")]
    [InlineData("missing", false, "MissingProduct")]
    [InlineData("component", false, "UnavailableProduct")]
    [InlineData("product-channel", false, "DeliveryDisabled")]
    [InlineData("category-channel", false, "DeliveryDisabled")]
    [InlineData("override-channel", true, "Available")]
    [InlineData("bundle", false, "UnsupportedChoices")]
    [InlineData("ingredient", false, "UnsupportedChoices")]
    [InlineData("required-sauce", false, "UnsupportedChoices")]
    [InlineData("variation-required", false, "VariationRequired")]
    [InlineData("active-variation", true, "Available")]
    [InlineData("inactive-variation", false, "UnavailableVariation")]
    [InlineData("foreign-variation", false, "UnavailableVariation")]
    [InlineData("base-degrades", true, "Available")]
    public async Task SnapshotUsesOperationalAvailabilityAndSupportedContract(string fault, bool available, string reason)
    {
        var order = await PrepareAsync(); var productId = order.Items[0].ProductId; Guid? variationId = null;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var product = await context.Products.Include(row => row.ProductCategories).ThenInclude(row => row.Category)
                .SingleAsync(row => row.Id == productId);
            if (fault is "category-channel" or "override-channel")
            {
                foreach (var assignment in product.ProductCategories) assignment.IsPrimary = false;
                var category = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = "Availability fixture",
                    CreatedBy = "test",
                    AvailableOrderTypes = (int)OrderChannels.Takeaway
                };
                context.Categories.Add(category);
                context.Set<ProductCategory>().Add(new()
                {
                    ProductId = productId,
                    CategoryId = category.Id,
                    Category = category,
                    IsPrimary = true,
                    CreatedBy = "test"
                });
            }
            switch (fault)
            {
                case "stock": product.IsAvailable = false; break;
                case "inactive": product.IsActive = false; break;
                case "deleted": product.IsDeleted = true; break;
                case "missing": productId = Guid.NewGuid(); break;
                case "component": product.IsComponent = true; break;
                case "product-channel": product.AvailableOrderTypes = (int)OrderChannels.Takeaway; break;
                case "category-channel": product.AvailableOrderTypes = null; break;
                case "override-channel": product.AvailableOrderTypes = (int)OrderChannels.Delivery; break;
                case "bundle": product.Type = ProductType.Menu; break;
                case "required-sauce": product.SauceMin = 1; break;
                case "ingredient": product.DetailedIngredients.Add(new() { Name = "Choice", CreatedBy = "test" }); break;
                case "variation-required":
                case "active-variation":
                case "inactive-variation":
                case "base-degrades":
                    var variation = new ProductVariation
                    {
                        Id = Guid.NewGuid(),
                        ProductId = productId,
                        Name = "Size",
                        CreatedBy = "test",
                        IsActive = fault is "variation-required" or "active-variation"
                    };
                    context.Set<ProductVariation>().Add(variation); product.HideBaseProduct = true;
                    if (fault is "active-variation" or "inactive-variation") variationId = variation.Id;
                    break;
                case "foreign-variation": variationId = Guid.NewGuid(); break;
            }
            await context.SaveChangesAsync();
        }
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.ChannelCatalogueRead]));
        var request = new ChannelAvailabilityRequest
        {
            Provider = order.Provider,
            StoreId = order.StoreId,
            Currency = "CHF",
            IsSandbox = true,
            Items = [new(productId, variationId)]
        };
        var snapshot = await Read(request); var item = snapshot.Items.Should().ContainSingle().Subject;
        item.ProductId.Should().Be(productId); item.VariationId.Should().Be(variationId);
        item.Available.Should().Be(available); item.Reason.Should().Be(reason);
        snapshot.Revision.Should().MatchRegex("^[a-f0-9]{64}$");
        await using var readback = DatabaseFixture.CreateContext();
        (await readback.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RepeatedOrReorderedSelectionKeepsRevisionWhileStockChangeRevisesIt()
    {
        var order = await PrepareAsync();
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.ChannelCatalogueRead]));
        var request = new ChannelAvailabilityRequest
        {
            Provider = order.Provider,
            StoreId = order.StoreId,
            Currency = "CHF",
            IsSandbox = true,
            Items = [new(order.Items[0].ProductId, null), new(Guid.NewGuid(), null)]
        };
        var original = await Read(request);
        (await Read(request with { Items = request.Items.AsEnumerable().Reverse().ToList() })).Revision.Should().Be(original.Revision);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var product = await context.Products.SingleAsync(row => row.Id == order.Items[0].ProductId);
            product.IsAvailable = false; await context.SaveChangesAsync();
        }
        (await Read(request)).Revision.Should().NotBe(original.Revision);
    }

    [Fact]
    public async Task MaximumBoundIncludesEveryMissingIdentityInsteadOfDroppingIt()
    {
        var order = await PrepareAsync();
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.ChannelCatalogueRead]));
        var request = new ChannelAvailabilityRequest
        {
            Provider = order.Provider,
            StoreId = order.StoreId,
            Currency = "CHF",
            IsSandbox = true,
            Items = [new(order.Items[0].ProductId, null),
                .. Enumerable.Range(0, 199).Select(_ => new ChannelAvailabilitySelection(Guid.NewGuid(), null))]
        };
        var snapshot = await Read(request);
        snapshot.Items.Should().HaveCount(200);
        snapshot.Items.Count(item => item.Available).Should().Be(1);
        snapshot.Items.Count(item => item.Reason == "MissingProduct").Should().Be(199);
    }

    private async Task<ChannelAvailabilitySnapshot> Read(ChannelAvailabilityRequest request)
    {
        var response = await PostAsJsonAsync(AvailabilityEndpoint, request);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ChannelAvailabilitySnapshot>(JsonOptions))!;
    }
}
