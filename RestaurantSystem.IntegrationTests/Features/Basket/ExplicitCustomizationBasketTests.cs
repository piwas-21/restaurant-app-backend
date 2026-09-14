using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 1")]
public class ExplicitCustomizationBasketTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Product _tacos = null!;
    private ProductCustomizationIngredientOption _sauce = null!;
    private ProductCustomizationProductOption _extraMeat = null!;

    public ExplicitCustomizationBasketTests(DatabaseFixture fixture) : base(fixture) { }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = await context.Categories.OrderBy(item => item.Name).FirstAsync();

        var meat = Product("Extra chicken", 0m, category.Id);
        _tacos = Product("Explicit Tacos", 10m, category.Id);
        var sauceIngredient = new ProductIngredient
        {
            Id = Guid.NewGuid(),
            Product = _tacos,
            ProductId = _tacos.Id,
            Name = "Algérienne",
            Price = 1m,
            IsOptional = true,
            IsActive = true,
            MaxQuantity = 1,
            CreatedBy = "test"
        };
        _tacos.DetailedIngredients.Add(sauceIngredient);

        var sauceGroup = Group(_tacos, "Sauces", min: 1, max: 2);
        _sauce = new ProductCustomizationIngredientOption
        {
            Id = Guid.NewGuid(),
            ProductCustomizationGroup = sauceGroup,
            ProductCustomizationGroupId = sauceGroup.Id,
            ProductIngredient = sauceIngredient,
            ProductIngredientId = sauceIngredient.Id,
            CreatedBy = "test"
        };
        sauceGroup.IngredientOptions.Add(_sauce);

        var extras = Group(_tacos, "Suppléments", min: 0, max: 1);
        _extraMeat = new ProductCustomizationProductOption
        {
            Id = Guid.NewGuid(),
            ProductCustomizationGroup = extras,
            ProductCustomizationGroupId = extras.Id,
            OptionProduct = meat,
            OptionProductId = meat.Id,
            AdditionalPrice = 3m,
            CreatedBy = "test"
        };
        extras.ProductOptions.Add(_extraMeat);
        _tacos.CustomizationGroups.Add(sauceGroup);
        _tacos.CustomizationGroups.Add(extras);

        context.Products.AddRange(meat, _tacos);
        await context.SaveChangesAsync();
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);
    }

    [Fact]
    public async Task MissingRequiredGroupSelection_IsRejected()
    {
        var response = await PostAsJsonAsync("/api/basket/items", Request(includeSauce: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MembershipFromAnotherGroup_IsRejected()
    {
        var request = Request(includeSauce: true);
        request.CustomizationSelections![0].Options[0] = new()
        {
            Kind = CustomizationOptionKind.Product,
            OptionId = _extraMeat.Id
        };

        var response = await PostAsJsonAsync("/api/basket/items", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidMixedSelection_IsServerPricedAndDeduplicated()
    {
        var first = await PostAsJsonAsync("/api/basket/items", Request(includeSauce: true, includeMeat: true));
        var second = await PostAsJsonAsync("/api/basket/items", Request(includeSauce: true, includeMeat: true));

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        first.StatusCode.Should().Be(HttpStatusCode.OK, firstBody);
        second.StatusCode.Should().Be(HttpStatusCode.OK, secondBody);
        var payload = await ReadResponseAsync<ApiResponse<BasketDto>>(second);
        var line = payload!.Data!.Items.Should().ContainSingle().Subject;
        line.Quantity.Should().Be(2);
        line.UnitPrice.Should().Be(14m);
        line.ItemTotal.Should().Be(28m);
        line.ChildItems.Should().ContainSingle()
            .Which.ProductCustomizationOptionId.Should().Be(_extraMeat.Id);
        line.ChildItems.Single().Quantity.Should().Be(2);
    }

    [Fact]
    public async Task StaffOrder_UsesTheSameGroupValidationPriceAndComposition()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = "Takeaway",
            items = new[]
            {
                new
                {
                    productId = _tacos.Id,
                    quantity = 1,
                    customizationSelections = Request(includeSauce: true, includeMeat: true)
                        .CustomizationSelections
                }
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await context.Orders.Include(item => item.Items).SingleAsync();
        order.Total.Should().Be(14m);
        order.Items.Should().ContainSingle(item => item.Kind == OrderItemKind.CustomizationOption)
            .Which.ProductId.Should().Be(_extraMeat.OptionProductId);
    }

    private AddToBasketDto Request(bool includeSauce, bool includeMeat = false)
    {
        var selections = new List<CustomizationGroupSelectionDto>();
        if (includeSauce)
        {
            selections.Add(new()
            {
                GroupId = _sauce.ProductCustomizationGroupId,
                Options = [new() { Kind = CustomizationOptionKind.Ingredient, OptionId = _sauce.Id }]
            });
        }
        if (includeMeat)
        {
            selections.Add(new()
            {
                GroupId = _extraMeat.ProductCustomizationGroupId,
                Options = [new() { Kind = CustomizationOptionKind.Product, OptionId = _extraMeat.Id }]
            });
        }
        return new() { ProductId = _tacos.Id, Quantity = 1, CustomizationSelections = selections };
    }

    private static ProductCustomizationGroup Group(Product product, string name, int min, int max) => new()
    {
        Id = Guid.NewGuid(),
        Product = product,
        ProductId = product.Id,
        Name = name,
        IsActive = true,
        IsRequired = min > 0,
        MinSelection = min,
        MaxSelection = max,
        CreatedBy = "test"
    };

    private static Product Product(string name, decimal price, Guid categoryId)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            BasePrice = price,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.MainItem,
            CreatedBy = "test"
        };
        product.ProductCategories.Add(new ProductCategory
        {
            ProductId = product.Id,
            CategoryId = categoryId,
            IsPrimary = true,
            CreatedBy = "test"
        });
        return product;
    }
}
