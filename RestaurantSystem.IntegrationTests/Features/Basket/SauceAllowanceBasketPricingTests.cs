using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// Slice S6 through the real add-to-basket path: <c>SauceIncludedFree</c> prices a line, and — the
/// decision S5 deliberately left open — <b>a bundle option follows the OPTION PRODUCT's own sauce
/// rule, not the parent bundle's.</b>
/// </summary>
/// <remarks>
/// <para>
/// The option IS that product — those are its sauce rows, priced from its own
/// <c>SauceIncludedFree</c> — and the parent bundle owns no sauce rows at all, so the parent-rule
/// alternative has nothing to apply a per-product allowance to.
/// </para>
/// <para>
/// The fixture makes the two rules disagree on purpose: the BUNDLE says three sauces are free and
/// the CHILD says one. If the parent's number were ever used, the guest would pay nothing and this
/// test would go red — which is the only way to prove which of the two the server reads.
/// </para>
/// <para>
/// It runs through <c>BasketItemFactory</c> (HTTP POST /api/basket/items), not through the pricing
/// service, because the wiring is the thing under test: the service is unit-tested in
/// <see cref="SauceIncludedFreePricingTests"/>.
/// </para>
/// </remarks>
[Collection("Database Lane 3")]
public class SauceAllowanceBasketPricingTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();

    private const decimal BundleBasePrice = 10.00m;
    private const decimal MainAdditional = 3.00m;

    private Product _bundle = null!;
    private MenuSection _mainSection = null!;
    private Guid _kebabId;
    private Guid _garlicSauceId;   // 0.50, paid
    private Guid _truffleSauceId;  // 2.50, paid — the dearest, so the free slot must land here
    private Guid _extraMeatId;     // 3.00, NOT a sauce — must never be waived

    public SauceAllowanceBasketPricingTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var categoryId = (await context.Categories.OrderBy(c => c.Name).FirstAsync()).Id;

        // The OPTION product: one free sauce of its own.
        var kebab = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Bundle Sauce Kebab",
            BasePrice = 15.00m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            SauceMin = 0,
            SauceMax = 3,
            SauceIncludedFree = 1,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _kebabId = kebab.Id;
        kebab.ProductCategories.Add(new ProductCategory
        {
            ProductId = kebab.Id,
            CategoryId = categoryId,
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        _garlicSauceId = Guid.NewGuid();
        kebab.DetailedIngredients.Add(NewRow(_garlicSauceId, kebab.Id, "Garlic Sauce", 0.50m, 0, IngredientKind.Sauce));
        _truffleSauceId = Guid.NewGuid();
        kebab.DetailedIngredients.Add(NewRow(_truffleSauceId, kebab.Id, "Truffle Sauce", 2.50m, 1, IngredientKind.Sauce));
        _extraMeatId = Guid.NewGuid();
        kebab.DetailedIngredients.Add(NewRow(_extraMeatId, kebab.Id, "Extra Meat", 3.00m, 2, IngredientKind.Ingredient));

        // The PARENT bundle claims three free sauces. Nothing may read it: a bundle has no sauce
        // rows for an allowance to apply to.
        var bundle = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Sauce Combo",
            BasePrice = BundleBasePrice,
            Type = ProductType.Menu,
            IsActive = true,
            IsAvailable = true,
            SauceIncludedFree = 3,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        bundle.ProductCategories.Add(new ProductCategory
        {
            ProductId = bundle.Id,
            CategoryId = categoryId,
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        var definition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = bundle.Id,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _mainSection = new MenuSection
        {
            Id = Guid.NewGuid(),
            MenuDefinitionId = definition.Id,
            Name = "Main",
            DisplayOrder = 1,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _mainSection.Items.Add(new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSectionId = _mainSection.Id,
            ProductId = kebab.Id,
            AdditionalPrice = MainAdditional,
            DisplayOrder = 1,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        definition.Sections.Add(_mainSection);
        bundle.MenuDefinition = definition;

        context.Products.AddRange(kebab, bundle);
        await context.SaveChangesAsync();

        _bundle = bundle;
    }

    private static ProductIngredient NewRow(
        Guid id, Guid productId, string name, decimal price, int displayOrder, IngredientKind kind) => new()
        {
            Id = id,
            ProductId = productId,
            Name = name,
            Kind = kind,
            IsOptional = true,
            IsIncludedInBasePrice = false,
            Price = price,
            MaxQuantity = 2,
            IsActive = true,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

    private Task<HttpResponseMessage> AddBundleAsync(List<Guid> selectedIngredients)
    {
        return PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _bundle.Id,
            Quantity = 1,
            SelectedMenuOptions = new List<SelectedMenuOptionDto>
            {
                new()
                {
                    SectionId = _mainSection.Id,
                    ItemId = _kebabId,
                    Quantity = 1,
                    SelectedIngredients = selectedIngredients
                }
            }
        });
    }

    [Fact]
    public async Task ABundleOption_IsPricedWithItsOwnProductsSauceAllowance()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        // Both sauces plus the (non-sauce) extra meat. The child product allows ONE free sauce, so
        // the dearer sauce — truffle at 2.50 — is waived and the guest pays 0.50 + 3.00.
        var response = await AddBundleAsync(
            new List<Guid> { _garlicSauceId, _truffleSauceId, _extraMeatId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);

        var parent = basket!.Data!.Items.Single(i => i.ProductId == _bundle.Id);
        var child = parent.ChildItems!.Single(c => c.ProductId == _kebabId);

        child.CustomizationPrice.Should().Be(3.50m,
            "the OPTION product's SauceIncludedFree = 1 waives the dearest sauce (2.50); " +
            "the parent bundle's SauceIncludedFree = 3 must not be read at all");

        // The parent line carries the child's customization price, so the money the guest sees is
        // the money the waiver produced.
        parent.CustomizationPrice.Should().Be(3.50m);
        parent.UnitPrice.Should().Be(BundleBasePrice + MainAdditional + 3.50m);
    }

    [Fact]
    public async Task ABundleOptionWithOnlyOneSauce_SpendsTheAllowanceOnIt()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        // One sauce, one free slot: the sauce costs nothing and the extra meat is untouched.
        var response = await AddBundleAsync(new List<Guid> { _garlicSauceId, _extraMeatId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        var parent = basket!.Data!.Items.Single(i => i.ProductId == _bundle.Id);
        var child = parent.ChildItems!.Single(c => c.ProductId == _kebabId);

        child.CustomizationPrice.Should().Be(3.00m, "only the sauce is waived, never the extra meat");
    }

    [Fact]
    public async Task ARegularItem_IsPricedWithItsOwnSauceAllowance()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        // The same product ordered on its own, not inside the bundle: BasketItemFactory's
        // regular-item path must read the same number. Two sauces plus the extra meat, one free.
        var response = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _kebabId,
            Quantity = 1,
            SelectedIngredients = new List<Guid> { _garlicSauceId, _truffleSauceId, _extraMeatId }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var basket = await ReadResponseAsync<ApiResponse<BasketDto>>(response);
        var line = basket!.Data!.Items.Single(i => i.ProductId == _kebabId);

        line.CustomizationPrice.Should().Be(3.50m, "the truffle sauce at 2.50 is the waived unit");
    }

    [Fact]
    public async Task TheBundleReadContract_CarriesTheOptionProductsOwnSauceRule()
    {
        // Change 3: the guest sheet has to be able to DISPLAY the rule the server prices with, or a
        // live "Add · CHF x" inside a bundle would disagree with the basket it produces.
        AuthenticateAsAdmin();
        var response = await Client.GetAsync($"/api/menus/{_bundle.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var read = await ReadResponseAsync<ApiResponse<MenuBundleDto>>(response);

        var option = read!.Data!
            .MenuDefinition!.Sections.Single()
            .Items.Single(i => i.ProductId == _kebabId);

        option.SauceMin.Should().Be(0);
        option.SauceMax.Should().Be(3, "null would mean no cap, which is not what this product says");
        option.SauceIncludedFree.Should().Be(1, "the OPTION product's number, not the bundle's 3");
    }
}
