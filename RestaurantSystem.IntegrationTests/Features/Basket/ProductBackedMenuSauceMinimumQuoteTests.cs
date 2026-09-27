using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos.Requests;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 3")]
public sealed class ProductBackedMenuSauceMinimumQuoteTests : IntegrationTestBase
{
    private const string Actor = "product-menu-sauce-minimum-quote-test";
    private static readonly Guid BundleId = Guid.NewGuid();
    private static readonly Guid ChildProductId = Guid.NewGuid();
    private static readonly Guid SectionId = Guid.NewGuid();
    private static readonly Guid SectionItemId = Guid.NewGuid();
    private static readonly Guid SauceGroupId = Guid.NewGuid();
    private static readonly Guid FirstSauceId = Guid.NewGuid();
    private static readonly Guid SecondSauceId = Guid.NewGuid();
    private static readonly Guid FirstSauceMembershipId = Guid.NewGuid();
    private static readonly Guid SecondSauceMembershipId = Guid.NewGuid();

    public ProductBackedMenuSauceMinimumQuoteTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<TenantFeatureSettings>(settings => settings.EnforceSauceMinimum = true);

    [Fact]
    public async Task BundleQuote_EnforcesNestedChildSauceMinimum_WithoutPersistingBasketOrOrderRows()
    {
        AuthenticateAsAdmin();
        var quote = await PostAsJsonAsync(
            $"/api/Products/{BundleId}/quote",
            QuoteRequest(FirstSauceMembershipId));

        quote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var failure = await ReadResponseAsync<ApiResponse<object>>(quote);
        failure!.ErrorCode.Should().Be("SauceMinimumNotMet");
        failure.Message.Should().Be("The selected sauces do not meet this item's minimum");

        var validQuoteResponse = await PostAsJsonAsync(
            $"/api/Products/{BundleId}/quote",
            QuoteRequest(FirstSauceMembershipId, SecondSauceMembershipId));
        validQuoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var validQuote = await ReadResponseAsync<ApiResponse<ProductQuoteDto>>(validQuoteResponse);
        validQuote!.Data!.UnitPrice.Should().Be(10m);
        validQuote.Data.TotalPrice.Should().Be(10m);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await context.Baskets.CountAsync()).Should().Be(0);
        (await context.BasketItems.CountAsync()).Should().Be(0);
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.OrderItems.CountAsync()).Should().Be(0);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = await context.Categories.FirstAsync();
        var child = ChildProduct(category.Id);
        var bundle = BundleProduct(category.Id);
        var definition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = BundleId,
            Product = bundle,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        var section = new MenuSection
        {
            Id = SectionId,
            MenuDefinitionId = definition.Id,
            MenuDefinition = definition,
            Name = "Choose a taco",
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        section.Items.Add(new MenuSectionItem
        {
            Id = SectionItemId,
            MenuSection = section,
            ProductId = ChildProductId,
            Product = child,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        definition.Sections.Add(section);
        bundle.MenuDefinition = definition;
        context.Products.AddRange(child, bundle);
        await context.SaveChangesAsync();
    }

    private static Product ChildProduct(Guid categoryId)
    {
        var product = NewProduct(ChildProductId, "Sauce minimum child", ProductType.MainItem, categoryId);
        product.IsComponent = true;
        product.SauceMin = 2;
        product.SauceMax = 2;
        product.SauceIncludedFree = 2;

        var firstSauce = Sauce(FirstSauceId, "First sauce", ChildProductId);
        var secondSauce = Sauce(SecondSauceId, "Second sauce", ChildProductId);
        product.DetailedIngredients.Add(firstSauce);
        product.DetailedIngredients.Add(secondSauce);

        var group = new ProductCustomizationGroup
        {
            Id = SauceGroupId,
            Product = product,
            ProductId = product.Id,
            Name = "Sauces",
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 2,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        group.IngredientOptions.Add(new ProductCustomizationIngredientOption
        {
            Id = FirstSauceMembershipId,
            ProductCustomizationGroup = group,
            ProductCustomizationGroupId = group.Id,
            ProductIngredient = firstSauce,
            ProductIngredientId = firstSauce.Id,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        group.IngredientOptions.Add(new ProductCustomizationIngredientOption
        {
            Id = SecondSauceMembershipId,
            ProductCustomizationGroup = group,
            ProductCustomizationGroupId = group.Id,
            ProductIngredient = secondSauce,
            ProductIngredientId = secondSauce.Id,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        product.CustomizationGroups.Add(group);
        return product;
    }

    private static Product BundleProduct(Guid categoryId) =>
        NewProduct(BundleId, "Sauce minimum bundle", ProductType.Menu, categoryId);

    private static ProductQuoteRequestDto QuoteRequest(params Guid[] sauceMembershipIds) => new()
    {
        Quantity = 1,
        SelectedMenuOptions =
        [
            new SelectedMenuOptionDto
            {
                SectionId = SectionId,
                ItemId = ChildProductId,
                Quantity = 1,
                CustomizationSelections =
                [
                    new CustomizationGroupSelectionDto
                    {
                        GroupId = SauceGroupId,
                        Options = sauceMembershipIds.Select(id => new CustomizationOptionSelectionDto
                        {
                            Kind = CustomizationOptionKind.Ingredient,
                            OptionId = id,
                            Quantity = 1
                        }).ToList()
                    }
                ]
            }
        ]
    };

    private static Product NewProduct(Guid id, string name, ProductType type, Guid categoryId)
    {
        var product = new Product
        {
            Id = id,
            Name = name,
            BasePrice = 10m,
            Type = type,
            IsActive = true,
            IsAvailable = true,
            Ingredients = [],
            Allergens = [],
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        product.ProductCategories.Add(new ProductCategory
        {
            ProductId = id,
            CategoryId = categoryId,
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        return product;
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
