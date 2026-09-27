using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos.Requests;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 3")]
public sealed class SauceMinimumEnforcementTests : IntegrationTestBase
{
    private const string Actor = "sauce-minimum-test";
    private const string SessionId = "8c0a79ce-c6c0-4b5a-9124-5093c63989d4";
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid SauceOneId = Guid.NewGuid();
    private static readonly Guid SauceTwoId = Guid.NewGuid();
    private static readonly Guid SauceThreeId = Guid.NewGuid();

    public SauceMinimumEnforcementTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<TenantFeatureSettings>(settings => settings.EnforceSauceMinimum = true);

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = await context.Categories.FirstAsync();
        var product = new Product
        {
            Id = ProductId,
            Name = "Sauce minimum test item",
            BasePrice = 10m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            SauceMin = 2,
            SauceMax = 3,
            Ingredients = [],
            Allergens = [],
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor,
            ProductCategories =
            [
                new ProductCategory { CategoryId = category.Id, IsPrimary = true, CreatedBy = Actor }
            ],
            DetailedIngredients =
            [
                Sauce(SauceOneId, "Sauce one", ProductId),
                Sauce(SauceTwoId, "Sauce two", ProductId),
                Sauce(SauceThreeId, "Sauce three", ProductId)
            ]
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GuestBasketAndQuote_EnforceMinimum_AndAcceptLegacyQuantityMaps()
    {
        AuthenticateAsAnonymous();
        Client.DefaultRequestHeaders.Add("X-Session-Id", SessionId);

        var oneSauce = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = ProductId,
            Quantity = 1,
            SelectedIngredients = [SauceOneId]
        });
        await AssertMinimumFailure(oneSauce);

        var twoSauces = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = ProductId,
            Quantity = 1,
            SelectedIngredients = [SauceOneId, SauceTwoId]
        });
        twoSauces.StatusCode.Should().Be(HttpStatusCode.OK);

        var oneLegacySauce = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = ProductId,
            Quantity = 1,
            IngredientQuantities = new Dictionary<Guid, int> { [SauceThreeId] = 1 }
        });
        await AssertMinimumFailure(oneLegacySauce);

        var twoLegacySauces = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = ProductId,
            Quantity = 1,
            IngredientQuantities = new Dictionary<Guid, int>
            {
                [SauceOneId] = 1,
                [SauceTwoId] = 2
            }
        });
        twoLegacySauces.StatusCode.Should().Be(HttpStatusCode.OK);

        var anonymousQuote = await PostAsJsonAsync($"/api/Products/{ProductId}/quote", new ProductQuoteRequestDto
        {
            Quantity = 1,
            SelectedIngredients = [SauceOneId, SauceTwoId]
        });
        anonymousQuote.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        AuthenticateAsAdmin();
        var quote = await PostAsJsonAsync($"/api/Products/{ProductId}/quote", new ProductQuoteRequestDto
        {
            Quantity = 1,
            SelectedIngredients = [SauceOneId]
        });
        await AssertMinimumFailure(quote);

        var validQuote = await PostAsJsonAsync($"/api/Products/{ProductId}/quote", new ProductQuoteRequestDto
        {
            Quantity = 1,
            SelectedIngredients = [SauceOneId, SauceTwoId]
        });
        validQuote.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WaiterAndStaffCounterPricing_EnforceTheSameMinimum()
    {
        AuthenticateAsRole(UserRole.Server);

        var waiterOrder = await PostAsJsonAsync("/api/Orders", new CreateOrderCommand
        {
            Type = OrderType.Takeaway,
            Items =
            [
                new CreateOrderItemDto
                {
                    ProductId = ProductId,
                    Quantity = 1,
                    SelectedIngredientIds = [SauceOneId]
                }
            ]
        });
        await AssertMinimumFailure(waiterOrder);

        AuthenticateAsRole(UserRole.Cashier);
        var counterQuote = await PostAsJsonAsync("/api/staff/orders/quote", new
        {
            type = OrderType.Takeaway,
            items = new[]
            {
                new CreateOrderItemDto
                {
                    ProductId = ProductId,
                    Quantity = 1,
                    SelectedIngredientIds = [SauceOneId]
                }
            }
        });
        await AssertMinimumFailure(counterQuote);

        var validCounterQuote = await PostAsJsonAsync("/api/staff/orders/quote", new
        {
            type = OrderType.Takeaway,
            items = new[]
            {
                new CreateOrderItemDto
                {
                    ProductId = ProductId,
                    Quantity = 1,
                    SelectedIngredientIds = [SauceOneId, SauceTwoId]
                }
            }
        });
        validCounterQuote.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task AssertMinimumFailure(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadResponseAsync<ApiResponse<object>>(response);
        body!.ErrorCode.Should().Be(ErrorCodes.SauceMinimumNotMet);
        body.Message.Should().Be("The selected sauces do not meet this item's minimum");
    }

    private static ProductIngredient Sauce(Guid id, string name, Guid productId) => new()
    {
        Id = id,
        ProductId = productId,
        Name = name,
        Kind = IngredientKind.Sauce,
        IsOptional = true,
        IsActive = true,
        IsIncludedInBasePrice = false,
        MaxQuantity = 1,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = Actor
    };
}
