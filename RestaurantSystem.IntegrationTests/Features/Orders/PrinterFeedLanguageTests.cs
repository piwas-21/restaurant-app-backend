using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using FeedQuery = RestaurantSystem.Api.Features.Orders.Queries.PrinterFeedQuery.PrinterFeedQuery;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// The feed's language parameter (2026-09-10 partner feedback): the print-language switch used to
/// translate the ticket's LABELS while the order DETAILS — product and ingredient names — printed
/// in the single language checkout froze, because the feed had no language to resolve.
/// <para>
/// Contract pinned here, one arm per branch of <c>OrderDisplayTranslator</c>: a translated name
/// resolves into the requested language, an untranslated one keeps the frozen checkout name (the
/// fallback, NOT an error), no parameter means today's single-language feed, and "auto" follows
/// the order's own PreferredLanguage within the print-safe set.
/// </para>
/// </summary>
[Collection("Database Lane 2")]
public class PrinterFeedLanguageTests : IntegrationTestBase
{
    private const string OrderNumber = "PF-LANG-1";
    private const string FrozenIngredientName = "Frites";
    private const string TranslatedIngredientName = "Frites maison";

    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly Guid TranslatedIngredientId = Guid.NewGuid();
    private static readonly Guid UntranslatedIngredientId = Guid.NewGuid();

    public PrinterFeedLanguageTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private async Task<List<OrderDto>> FetchFeedAsync(string? language)
    {
        using var scope = Factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();
        return await mediator.SendQuery<FeedQuery, List<OrderDto>>(new FeedQuery(ModifiedSince: null, Language: language));
    }

    private OrderDto OrderFor(List<OrderDto> feed) => feed.Single(o => o.OrderNumber == OrderNumber);

    [Fact]
    public async Task NoLanguage_ServesTheFrozenNames_Unchanged()
    {
        var item = OrderFor(await FetchFeedAsync(null)).Items.Single();

        item.ProductName.Should().Be("Poulet Grillé");
        item.IngredientCustomizations!.Should().Contain(c =>
            c.IngredientId == TranslatedIngredientId && c.IngredientName == FrozenIngredientName && c.IsRemoved);
        item.IngredientCustomizations!.Should().Contain(c =>
            c.IngredientId == UntranslatedIngredientId && c.IngredientName == "Sauce Algérienne" && c.IsAddOn);
    }

    [Fact]
    public async Task FixedLanguage_TranslatesNamesAndFallsBackToFrozenWhenUntranslated()
    {
        var item = OrderFor(await FetchFeedAsync("fr")).Items.Single();

        // The product carries a French description — the name resolves into it.
        item.ProductName.Should().Be("Poulet grillé maison");
        // The chosen ingredient carries a French description too.
        item.IngredientCustomizations!.Single(c => c.IngredientId == TranslatedIngredientId)
            .IngredientName.Should().Be(TranslatedIngredientName);
        // The other one has none: the FROZEN checkout name is the fallback, not an error —
        // and critically, IsRemoved/IsAddOn/Quantity still come off the snapshot untouched.
        var fallback = item.IngredientCustomizations!.Single(c => c.IngredientId == UntranslatedIngredientId);
        fallback.IngredientName.Should().Be("Sauce Algérienne");
        fallback.IsAddOn.Should().BeTrue();
        fallback.Quantity.Should().Be(1);
    }

    [Fact]
    public async Task Auto_FollowsTheOrdersPreferredLanguage_WithinThePrintSafeSet()
    {
        var item = OrderFor(await FetchFeedAsync("auto")).Items.Single();
        item.ProductName.Should().Be("Poulet grillé maison", "the order's preferred language is fr — print-safe");

        // An order whose preferred language cannot encode on the receipt codepage keeps the
        // frozen (Latin) names instead of printing garbage.
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await context.Orders.SingleAsync(o => o.OrderNumber == OrderNumber);
        order.PreferredLanguage = "ar";
        await context.SaveChangesAsync();

        var after = OrderFor(await FetchFeedAsync("auto")).Items.Single();
        after.ProductName.Should().Be("Poulet Grillé", "ar is outside the print-safe set — frozen names stay");
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var product = new Product
        {
            Id = ProductId,
            Name = "Poulet Grillé",
            BasePrice = 16.00m,
            Type = ProductType.MainItem,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            IsActive = true,
            IsAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        product.Descriptions.Add(new ProductDescription
        {
            ProductId = ProductId,
            Lang = "fr",
            Name = "Poulet grillé maison",
            Description = "",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        product.Descriptions.Add(new ProductDescription
        {
            ProductId = ProductId,
            Lang = "en",
            Name = "Grilled Chicken",
            Description = "",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        // REQUIRED, and the fixture deselects it below — a removal is a decision, so the row
        // survives the display rule (backend #517) and carries the translation.
        var translated = new ProductIngredient
        {
            Id = TranslatedIngredientId,
            ProductId = ProductId,
            Name = FrozenIngredientName,
            IsOptional = false,
            MaxQuantity = 1,
            IsActive = true,
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        translated.Descriptions.Add(new ProductIngredientDescription
        {
            ProductIngredientId = TranslatedIngredientId,
            LanguageCode = "fr",
            Name = TranslatedIngredientName,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        // An OPTIONAL PAID EXTRA (chosen at quantity 1 below) — the other kind of decision that
        // renders. It carries NO French description, which is the fallback half of the contract.
        var untranslated = new ProductIngredient
        {
            Id = UntranslatedIngredientId,
            ProductId = ProductId,
            Name = "Sauce Algérienne",
            IsOptional = true,
            IsIncludedInBasePrice = false,
            MaxQuantity = 1,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        product.DetailedIngredients.Add(translated);
        product.DetailedIngredients.Add(untranslated);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = OrderNumber,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            PreferredLanguage = "fr",
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        order.Items.Add(new OrderItem
        {
            Id = ItemId,
            OrderId = order.Id,
            ProductId = ProductId,
            ProductName = "Poulet Grillé",
            Quantity = 1,
            UnitPrice = 16.00m,
            ItemTotal = 16.00m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        order.Items.Single().IngredientSnapshots.Add(new OrderItemIngredient
        {
            IngredientId = TranslatedIngredientId,
            IngredientName = FrozenIngredientName,
            Quantity = 0,
            IsRemoved = true,
            IsAddOn = false,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        order.Items.Single().IngredientSnapshots.Add(new OrderItemIngredient
        {
            IngredientId = UntranslatedIngredientId,
            IngredientName = "Sauce Algérienne",
            Quantity = 1,
            IsRemoved = false,
            IsAddOn = true,
            SortOrder = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        context.AddRange(product, order);
        await context.SaveChangesAsync();
    }
}
