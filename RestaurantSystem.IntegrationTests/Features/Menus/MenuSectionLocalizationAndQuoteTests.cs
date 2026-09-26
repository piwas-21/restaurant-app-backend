using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos.Requests;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

[Collection("Database Lane 2")]
public sealed class MenuSectionLocalizationAndQuoteTests : IntegrationTestBase
{
    private const string Actor = "menu-localization-quote-test";
    private static readonly Guid BundleId = Guid.NewGuid();
    private static readonly Guid ComponentId = Guid.NewGuid();
    private static readonly Guid SectionId = Guid.NewGuid();
    private static readonly Guid SectionItemId = Guid.NewGuid();
    private static readonly Guid VariationId = Guid.NewGuid();
    private static readonly Guid IngredientId = Guid.NewGuid();
    private const string CanonicalSectionName = "Choose meat";

    public MenuSectionLocalizationAndQuoteTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task SectionTranslationsRoundTripAndLocaleFallsBackToLanguageThenCanonicalName()
    {
        AuthenticateAsAdmin();
        Client.DefaultRequestHeaders.Add("If-Match", "\"1\"");

        var patch = await PatchAsJsonAsync($"/api/Menus/{BundleId}/sections", new
        {
            sections = new[]
            {
                new
                {
                    id = SectionId,
                    name = CanonicalSectionName,
                    description = "Choose one meat",
                    displayOrder = 0,
                    isRequired = true,
                    minSelection = 1,
                    maxSelection = 1,
                    translations = new Dictionary<string, MenuSectionTranslationDto>
                    {
                        ["fr"] = new() { Name = "Choisir une viande", Description = "Choisissez une viande" }
                    }
                }
            }
        });

        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var patchResult = await ReadResponseAsync<ApiResponse<MenuSectionsPatchResultDto>>(patch);
        var firstAuthoringVersion = patchResult!.Data!.AuthoringVersion;
        patchResult.Data.Sections.Single().Translations.Should().ContainKey("fr");

        var bundleRead = await GetFromJsonAsync<ApiResponse<MenuBundleDto>>(
            $"/api/Menus/{BundleId}?locale=fr-CH");
        var bundleSection = bundleRead!.Data!.MenuDefinition!.Sections.Single();
        bundleSection.Name.Should().Be(CanonicalSectionName);
        bundleSection.DisplayName.Should().Be("Choisir une viande");
        bundleSection.DisplayDescription.Should().Be("Choisissez une viande");

        var productRead = await GetFromJsonAsync<ApiResponse<ProductDto>>(
            $"/api/Products/{BundleId}?locale=de-CH");
        var productSection = productRead!.Data!.MenuDefinition!.Sections.Single();
        productSection.Name.Should().Be(CanonicalSectionName);
        productSection.DisplayName.Should().Be(CanonicalSectionName,
            "an unavailable translation falls back to the operational canonical label");
        productSection.Translations.Should().ContainKey("fr");

        Client.DefaultRequestHeaders.Remove("If-Match");
        Client.DefaultRequestHeaders.Add("If-Match", $"\"{firstAuthoringVersion}\"");
        var omittedTranslations = await PatchAsJsonAsync($"/api/Menus/{BundleId}/sections", new
        {
            sections = new[]
            {
                new
                {
                    id = SectionId,
                    name = "Pick a meat",
                    description = "Choose one meat",
                    displayOrder = 0,
                    isRequired = true,
                    minSelection = 1,
                    maxSelection = 1
                }
            }
        });
        omittedTranslations.StatusCode.Should().Be(HttpStatusCode.OK,
            "omitting the localized map means it is unchanged on the stable-ID patch");

        Client.DefaultRequestHeaders.Remove("If-Match");
        Client.DefaultRequestHeaders.Add("If-Match", "\"3\"");
        var clearedTranslations = await PatchAsJsonAsync($"/api/Menus/{BundleId}/sections", new
        {
            sections = new[]
            {
                new
                {
                    id = SectionId,
                    name = "Pick a meat",
                    description = "Choose one meat",
                    displayOrder = 0,
                    isRequired = true,
                    minSelection = 1,
                    maxSelection = 1,
                    translations = new Dictionary<string, MenuSectionTranslationDto>()
                }
            }
        });
        clearedTranslations.StatusCode.Should().Be(HttpStatusCode.OK,
            "an explicit empty localized map removes the saved translations");

        var clearedRead = await GetFromJsonAsync<ApiResponse<MenuBundleDto>>(
            $"/api/Menus/{BundleId}?locale=fr-CH");
        clearedRead!.Data!.MenuDefinition!.Sections.Single().Translations.Should().BeEmpty();
        clearedRead.Data.MenuDefinition.Sections.Single().DisplayName.Should().Be("Pick a meat");
    }

    [Fact]
    public async Task BundleQuoteMatchesBasketFactoryAndDoesNotCreateBasketOrOrderRows()
    {
        AuthenticateAsAdmin();
        var request = new ProductQuoteRequestDto
        {
            Quantity = 2,
            SelectedMenuOptions =
            [
                new SelectedMenuOptionDto
                {
                    SectionId = SectionId,
                    ItemId = ComponentId,
                    ProductVariationId = VariationId,
                    Quantity = 1,
                    SelectedIngredients = [IngredientId],
                    IngredientQuantities = new Dictionary<Guid, int> { [IngredientId] = 2 }
                }
            ]
        };

        var quoteResponse = await PostAsJsonAsync($"/api/Products/{BundleId}/quote", request);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = await ReadResponseAsync<ApiResponse<ProductQuoteDto>>(quoteResponse);
        quote!.Data!.UnitPrice.Should().Be(15m);
        quote.Data.TotalPrice.Should().Be(30m);

        AuthenticateAsAnonymous();
        var anonymousQuote = await PostAsJsonAsync($"/api/Products/{BundleId}/quote", request);
        anonymousQuote.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Baskets.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await context.BasketItems.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await context.Orders.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        }

        var sessionId = Guid.NewGuid().ToString();
        Client.DefaultRequestHeaders.Add("X-Session-Id", sessionId);
        var basketResponse = await PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = BundleId,
            Quantity = request.Quantity,
            SelectedMenuOptions = request.SelectedMenuOptions
        });
        basketResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var finalScope = Factory.Services.CreateScope();
        var finalContext = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var basket = await finalContext.Baskets.Include(value => value.Items)
            .SingleAsync(value => value.SessionId == sessionId);
        basket.SubTotal.Should().Be(quote.Data.TotalPrice);
        basket.Items.Single(item => item.ParentBasketItemId == null).UnitPrice.Should().Be(quote.Data.UnitPrice);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var category = await context.Categories.FirstAsync();

        var component = NewProduct(ComponentId, "Quote meat component", ProductType.MainItem, category, 0m, true);
        var variation = new ProductVariation
        {
            Id = VariationId,
            ProductId = ComponentId,
            Product = component,
            Name = "Large portion",
            PriceModifier = 2m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        component.Variations.Add(variation);
        component.DetailedIngredients.Add(new ProductIngredient
        {
            Id = IngredientId,
            ProductId = ComponentId,
            Product = component,
            Name = "Extra sauce",
            IsOptional = true,
            IsActive = true,
            MaxQuantity = 3,
            Price = 1m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });

        var bundle = NewProduct(BundleId, "Quote bundle", ProductType.Menu, category, 10m, false);
        var section = new MenuSection
        {
            Id = SectionId,
            MenuDefinitionId = Guid.NewGuid(),
            Name = CanonicalSectionName,
            Description = "Choose one meat",
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
            ProductId = ComponentId,
            Product = component,
            ProductVariationId = VariationId,
            ProductVariation = variation,
            AdditionalPrice = 1m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        var definition = new MenuDefinition
        {
            Id = section.MenuDefinitionId,
            ProductId = BundleId,
            Product = bundle,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor,
            Sections = [section]
        };
        bundle.MenuDefinition = definition;
        context.AddRange(component, bundle);
        context.Add(definition);
        await context.SaveChangesAsync();
    }

    private static Product NewProduct(
        Guid id,
        string name,
        ProductType type,
        Category category,
        decimal basePrice,
        bool isComponent)
    {
        var product = new Product
        {
            Id = id,
            Name = name,
            BasePrice = basePrice,
            Type = type,
            IsComponent = isComponent,
            IsActive = true,
            IsAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        };
        product.ProductCategories.Add(new ProductCategory
        {
            Product = product,
            Category = category,
            IsPrimary = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor
        });
        return product;
    }

    private Task<HttpResponseMessage> PatchAsJsonAsync<T>(string requestUri, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return Client.PatchAsync(requestUri, content);
    }
}
