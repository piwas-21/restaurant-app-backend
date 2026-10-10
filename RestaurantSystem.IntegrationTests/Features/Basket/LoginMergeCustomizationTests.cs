using System.Data.Common;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// Issue #313: the login merge matches an anonymous line to a user line on identity alone —
// ParentBasketItemId, ProductId, ProductVariationId — with no customization comparison. The ADD path
// runs the same identity query and then filters it with IsSameCustomization, deliberately, so that
// two differently-customised lines of the same product stay separate (#155).
//
// Two notions of "the same line" in one codebase is the defect; the visible damage is that a guest's
// paid extras vanish at login, or that extras nobody asked for appear.
//
// WHY THE EXISTING MERGE SUITE IS BLIND: AnonymousBasketMergeIntegrationTest adds every line through
// `new AddToBasketDto { ProductId, Quantity }` — identical and uncustomised on both sides — which is
// exactly the case where matching on identity alone gives the right answer.
//
// Customization here is a SIDE ITEM rather than an ingredient, following BasketLineTotalTests: it is
// priced unconditionally by BuildRegularItemAsync, so the fixture does not depend on the ingredient
// rules and their optional / included-in-base branches.
[Collection("Database Lane 4")]
public class LoginMergeCustomizationTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private readonly Guid _userId = Guid.Parse(TestAuthHandler.UserId);
    private Product _pizza = null!;
    private Product _cola = null!;

    public LoginMergeCustomizationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _pizza = await context.Products.FirstAsync(p => p.Name == "Test Pizza");
        _cola = await context.Products.FirstAsync(p => p.Name == "Test Cola");
    }

    private async Task AddPlainAsync(string? sessionId, Guid? userId, int quantity)
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(sessionId!, userId,
            new AddToBasketDto { ProductId = _pizza.Id, Quantity = quantity });
    }

    private async Task AddCustomisedAsync(string? sessionId, Guid? userId, int quantity)
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(sessionId!, userId, new AddToBasketDto
        {
            ProductId = _pizza.Id,
            Quantity = quantity,
            SelectedSideItems = new List<SelectedSideItemDto>
            {
                new() { Id = _cola.Id, Quantity = 1 }
            }
        });
    }

    private async Task MergeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.MergeAnonymousBasketAsync(_sessionId, _userId);
    }

    /// <summary>
    /// The surviving user basket's ROOT rows, read from the database in a fresh scope. Read from
    /// stored state rather than the returned DTO: the merge's damage is a hard DELETE plus a quantity
    /// mutation, and only the rows can show that a line is gone rather than merely unmapped.
    /// </summary>
    private async Task<List<BasketItem>> ReadUserRootsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var basket = await context.Baskets
            .Include(b => b.Items)
            .SingleAsync(b => b.UserId == _userId && !b.IsDeleted);

        return basket.Items.Where(i => i.ParentBasketItemId == null).ToList();
    }

    private decimal PlainLineTotal(int quantity) => _pizza.BasePrice * quantity;
    private decimal CustomisedLineTotal(int quantity) => (_pizza.BasePrice + _cola.BasePrice) * quantity;

    // The guest's extras must not be deleted by logging in. Anonymous line is customised, the user's
    // is plain: matching on identity alone keeps the USER's row, adds the anonymous quantity to it,
    // and hard-deletes the anonymous row — the side item is gone, unpaid for and unprinted.
    [Fact]
    public async Task CustomisedAnonymousLine_DoesNotMergeIntoAPlainUserLine()
    {
        await AddCustomisedAsync(_sessionId, null, 1);
        await AddPlainAsync(null, _userId, 1);

        await MergeAsync();

        var roots = await ReadUserRootsAsync();

        roots.Should().HaveCount(2, "a customised line and a plain line are not the same line — the add path has always treated them as distinct");

        var customised = roots.Should().ContainSingle(r => r.CustomizationPrice != 0m).Subject;
        customised.Quantity.Should().Be(1);
        customised.CustomizationPrice.Should().Be(_cola.BasePrice);
        customised.SelectedSideItemsJson.Should().NotBeNullOrEmpty("the guest paid for this side item");
        customised.ItemTotal.Should().Be(CustomisedLineTotal(1));

        var plain = roots.Should().ContainSingle(r => r.CustomizationPrice == 0m).Subject;
        plain.Quantity.Should().Be(1);
        plain.ItemTotal.Should().Be(PlainLineTotal(1));
    }

    // The mirror, which is the worse half: the guest's PLAIN unit is absorbed into the user's
    // customised line, so they are charged for a side item they never chose and the kitchen makes it.
    [Fact]
    public async Task PlainAnonymousLine_DoesNotMergeIntoACustomisedUserLine()
    {
        await AddPlainAsync(_sessionId, null, 1);
        await AddCustomisedAsync(null, _userId, 1);

        await MergeAsync();

        var roots = await ReadUserRootsAsync();

        roots.Should().HaveCount(2, "a plain line must not inherit the extras of a customised one");

        var customised = roots.Should().ContainSingle(r => r.CustomizationPrice != 0m).Subject;
        customised.Quantity.Should().Be(1, "the anonymous plain unit must NOT be added to the customised line");
        customised.ItemTotal.Should().Be(CustomisedLineTotal(1));

        var plain = roots.Should().ContainSingle(r => r.CustomizationPrice == 0m).Subject;
        plain.Quantity.Should().Be(1);
        plain.SelectedSideItemsJson.Should().BeNullOrEmpty("nothing was chosen on this line");
        plain.ItemTotal.Should().Be(PlainLineTotal(1));
    }

    // Identical customization on both sides MUST still merge. This is the case the merge exists for,
    // and it is the one a fix could easily break by refusing every match — which would leave the guest
    // with two rows where they expect one, and is why it is pinned alongside the two above.
    [Fact]
    public async Task IdenticallyCustomisedLines_StillMerge()
    {
        await AddCustomisedAsync(_sessionId, null, 1);
        await AddCustomisedAsync(null, _userId, 2);

        await MergeAsync();

        var roots = await ReadUserRootsAsync();

        var line = roots.Should().ContainSingle("same product, same customization — one line").Subject;
        line.Quantity.Should().Be(3);
        line.CustomizationPrice.Should().Be(_cola.BasePrice);
        line.ItemTotal.Should().Be(CustomisedLineTotal(3),
            "the merged total goes through BasketLineTotal.ForRoot, so the side item is charged per unit (#308)");
    }

    // Plain-into-plain: the behaviour the existing merge suite already covers, pinned here so a fix
    // that over-refuses is caught on the simplest possible input.
    [Fact]
    public async Task IdenticalPlainLines_StillMerge()
    {
        await AddPlainAsync(_sessionId, null, 1);
        await AddPlainAsync(null, _userId, 2);

        await MergeAsync();

        var roots = await ReadUserRootsAsync();

        var line = roots.Should().ContainSingle().Subject;
        line.Quantity.Should().Be(3);
        line.ItemTotal.Should().Be(PlainLineTotal(3));
    }

    [Fact]
    public async Task BasketRead_BatchesRootAndNestedSidesWithoutChangingSavedTotals()
    {
        var sideProductId = Guid.NewGuid();
        const decimal sideBasePrice = 2.99m;
        var regularId = Guid.NewGuid();
        var largeId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sideProduct = new Product
            {
                Id = sideProductId,
                Name = "Batch-read drink",
                BasePrice = sideBasePrice,
                Type = ProductType.Beverage,
                IsActive = true,
                IsAvailable = true,
                Ingredients = [],
                Allergens = [],
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            };
            sideProduct.Variations.Add(new ProductVariation
            {
                Id = regularId,
                ProductId = sideProduct.Id,
                Name = "Regular drink",
                PriceModifier = 0.50m,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });
            sideProduct.Variations.Add(new ProductVariation
            {
                Id = largeId,
                ProductId = sideProduct.Id,
                Name = "Large drink",
                PriceModifier = 1.25m,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });
            context.Products.Add(sideProduct);
            await context.SaveChangesAsync();
        }

        var basketId = Guid.NewGuid();
        var bundle = MappingLine(_pizza, basketId, 2,
            SideJson(sideProductId, regularId, 2, CompositionRole.Drink, 4));
        var child = MappingLine(_pizza, basketId, 4,
            SideJson(sideProductId, largeId, 3, CompositionRole.Side, 1), bundle.Id);
        var secondRoot = MappingLine(_pizza, basketId, 1,
            SideJson(sideProductId, largeId, 1, CompositionRole.Drink, 8));
        bundle.ChildBasketItems.Add(child);

        var basket = new RestaurantSystem.Domain.Entities.Basket
        {
            Id = basketId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            SessionId = _sessionId,
            SubTotal = 47.25m,
            Total = 48.10m,
            Items = [bundle, child, secondRoot]
        };
        var counter = new SelectCommandCounter();
        await using var mappingContext = DatabaseFixture.CreateContext(counter);
        var translator = new Mock<IBasketToOrderTranslator>();
        translator.Setup(value => value.Translate(It.IsAny<IEnumerable<BasketItemDto>>()))
            .Returns(new List<CreateOrderItemDto>());
        var mapper = new BasketMappingService(mappingContext, Mock.Of<ICustomerDiscountService>(),
            NullLogger<BasketMappingService>.Instance, translator.Object);

        var mapped = await mapper.MapAsync(basket);

        mapped.Items.Should().HaveCount(2);
        mapped.SubTotal.Should().Be(47.25m);
        mapped.Total.Should().Be(48.10m, "side display projection must not recalculate the server-saved basket total");
        var mappedBundle = mapped.Items.Single(item => item.Id == bundle.Id);
        var mappedBundleSide = mappedBundle.SelectedSideItems!.Should().ContainSingle().Subject;
        mappedBundleSide.Quantity.Should().Be(2);
        mappedBundleSide.VariationName.Should().Be("Regular drink");
        mappedBundleSide.CompositionRole.Should().Be(CompositionRole.Drink);
        mappedBundleSide.SubTotal.Should().Be((sideBasePrice + 0.50m) * 2);

        var mappedChildSide = mappedBundle.ChildItems!.Should().ContainSingle().Subject
            .SelectedSideItems!.Should().ContainSingle().Subject;
        mappedChildSide.Quantity.Should().Be(3);
        mappedChildSide.VariationName.Should().Be("Large drink");
        mappedChildSide.CompositionRole.Should().Be(CompositionRole.Side);
        mappedChildSide.PresentationOrder.Should().Be(1);
        mappedChildSide.SubTotal.Should().Be((sideBasePrice + 1.25m) * 3);

        var mappedSecondRootSide = mapped.Items.Single(item => item.Id == secondRoot.Id)
            .SelectedSideItems!.Should().ContainSingle().Subject;
        mappedSecondRootSide.Quantity.Should().Be(1);
        mappedSecondRootSide.VariationName.Should().Be("Large drink");
        mappedSecondRootSide.CompositionRole.Should().Be(CompositionRole.Drink);
        mappedSecondRootSide.PresentationOrder.Should().Be(8);
        counter.ReadCount.Should().Be(1, "side products and variations are loaded in one batch for all roots and children");
    }

    private static BasketItem MappingLine(
        Product product, Guid basketId, int quantity, string sidesJson, Guid? parentItemId = null) => new()
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            BasketId = basketId,
            ProductId = product.Id,
            Product = product,
            ParentBasketItemId = parentItemId,
            Quantity = quantity,
            UnitPrice = product.BasePrice,
            ItemTotal = product.BasePrice * quantity,
            SelectedSideItemsJson = sidesJson
        };

    private static string SideJson(Guid productId, Guid variationId, int quantity,
        CompositionRole role, int presentationOrder) => JsonSerializer.Serialize(new[]
        {
            new SelectedSideItemDto
            {
                Id = productId,
                ProductVariationId = variationId,
                Quantity = quantity,
                CompositionRole = role,
                PresentationOrder = presentationOrder
            }
        });

    private sealed class SelectCommandCounter : DbCommandInterceptor
    {
        private int _readCount;
        public int ReadCount => Volatile.Read(ref _readCount);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                Interlocked.Increment(ref _readCount);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
