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
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// Issue #155 (menu-bundles redesign slice 3): the AddToBasket dedup compared special
// instructions + selected/excluded ingredients but NOT the top-level side items or the
// per-ingredient quantities — so two otherwise-identical lines that differed only by their
// sides (or by an ingredient's quantity) silently merged into one. These pin the fix.
public class BasketDedupTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Product _pizza = null!;
    private Product _fries = null!;
    private Product _coke = null!;
    private ProductIngredient _cheese = null!;

    public BasketDedupTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _pizza = MakeProduct("Dedup Pizza", 12m);
        _cheese = new ProductIngredient
        {
            Id = Guid.NewGuid(),
            ProductId = _pizza.Id,
            Name = "Cheese",
            IsOptional = true,
            IsIncludedInBasePrice = false,
            Price = 1m,
            MaxQuantity = 3,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _pizza.DetailedIngredients.Add(_cheese);

        _fries = MakeProduct("Dedup Fries", 4m); // used as a top-level side item
        _coke = MakeProduct("Dedup Coke", 3m); // used as a different top-level side item

        context.Products.AddRange(_pizza, _fries, _coke);
        await context.SaveChangesAsync();
    }

    private static Product MakeProduct(string name, decimal price) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        BasePrice = price,
        Type = ProductType.MainItem,
        IsActive = true,
        IsAvailable = true,
        Ingredients = new List<string>(),
        Allergens = new List<string>(),
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test"
    };

    private async Task<BasketDto> AddAsync(AddToBasketDto dto)
    {
        var response = await PostAsJsonAsync("/api/basket/items", dto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        return basket!.Data!;
    }

    private int PizzaLineCount(BasketDto basket) => basket.Items.Count(i => i.ProductId == _pizza.Id);

    [Fact]
    public async Task SameProduct_DifferentSideItems_DoNotMerge()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        await AddAsync(new AddToBasketDto
        {
            ProductId = _pizza.Id,
            Quantity = 1,
            SelectedIngredients = new List<Guid> { _cheese.Id },
            SelectedSideItems = new List<SelectedSideItemDto> { new() { Id = _fries.Id, Quantity = 1 } },
        });

        var basket = await AddAsync(new AddToBasketDto
        {
            ProductId = _pizza.Id,
            Quantity = 1,
            SelectedIngredients = new List<Guid> { _cheese.Id },
            SelectedSideItems = new List<SelectedSideItemDto> { new() { Id = _coke.Id, Quantity = 1 } },
        });

        PizzaLineCount(basket).Should().Be(2, "the two lines differ by their side item");
    }

    // Issue #188. The dedup helpers deserialized the basket's own JSON columns UNGUARDED while
    // the read path (BasketMappingService, since #169) has always caught JsonException on the
    // very same columns — so one malformed value turned add-to-basket into a 500 on the write
    // path and a logged warning on the read path. These corrupt the stored JSON directly, which
    // is the only way to reach the branch: nothing in the API can write a malformed value, and
    // that is exactly why it went unnoticed.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MalformedStoredJson_DoesNotFailTheAdd_AndDoesNotMergeIntoTheUnreadableLine(bool corruptSideItems)
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        AddToBasketDto Payload() => new()
        {
            ProductId = _pizza.Id,
            Quantity = 1,
            SelectedIngredients = new List<Guid> { _cheese.Id },
            SelectedSideItems = new List<SelectedSideItemDto> { new() { Id = _fries.Id, Quantity = 1 } },
            IngredientQuantities = new Dictionary<Guid, int> { [_cheese.Id] = 2 },
        };

        await AddAsync(Payload());

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await context.BasketItems.FirstAsync(i => i.ProductId == _pizza.Id);
            if (corruptSideItems)
            {
                stored.SelectedSideItemsJson = "{ not json";
            }
            else
            {
                stored.IngredientQuantitiesJson = "{ not json";
            }

            await context.SaveChangesAsync();
        }

        // The bug: this call used to throw JsonException -> 500. AddAsync asserts 200.
        var basket = await AddAsync(Payload());

        // And the answer to "is this the same customization?" is not-equal, deliberately.
        // Merging would increment a line whose customization nobody can read, discarding what
        // this customer actually chose while charging them for two of it.
        PizzaLineCount(basket).Should().Be(2, "an unreadable stored customization cannot be judged equal");
    }

    [Fact]
    public async Task SameProduct_DifferentIngredientQuantities_DoNotMerge()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        await AddAsync(new AddToBasketDto
        {
            ProductId = _pizza.Id,
            Quantity = 1,
            SelectedIngredients = new List<Guid> { _cheese.Id },
            IngredientQuantities = new Dictionary<Guid, int> { [_cheese.Id] = 1 },
        });

        var basket = await AddAsync(new AddToBasketDto
        {
            ProductId = _pizza.Id,
            Quantity = 1,
            SelectedIngredients = new List<Guid> { _cheese.Id },
            IngredientQuantities = new Dictionary<Guid, int> { [_cheese.Id] = 2 },
        });

        PizzaLineCount(basket).Should().Be(2, "the two lines differ by cheese quantity (×1 vs ×2)");
    }

    [Fact]
    public async Task SameProduct_IdenticalCustomization_MergesToOneLine()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var payload = () => new AddToBasketDto
        {
            ProductId = _pizza.Id,
            Quantity = 1,
            SelectedIngredients = new List<Guid> { _cheese.Id },
            IngredientQuantities = new Dictionary<Guid, int> { [_cheese.Id] = 2 },
            SelectedSideItems = new List<SelectedSideItemDto> { new() { Id = _fries.Id, Quantity = 1 } },
        };

        await AddAsync(payload());
        var basket = await AddAsync(payload());

        PizzaLineCount(basket).Should().Be(1, "identical customizations still dedup");
        basket.Items.Single(i => i.ProductId == _pizza.Id).Quantity.Should().Be(2);
    }
}
