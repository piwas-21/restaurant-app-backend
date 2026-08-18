using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// Plan §9.11. PUT /api/Basket/order-type answers in two phases, and the BLOCKED phase used to map
// the basket loaded by FindTrackedBasketWithItemsAsync — an .Items-only include. Every product read
// in BasketMappingService is null-conditional, so nothing threw: the response simply carried
// ProductName = "" on every line. The success phase re-read the full graph, which is why it looked
// fine. These are integration tests against a real database on purpose — the bug is entirely in
// which navigations EF loaded, so a mocked repository would reproduce nothing.
[Collection("Database Lane 3")]
public class BasketChannelSwitchPayloadTests : IntegrationTestBase
{
    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);

    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Guid _pizzaId;
    private Guid _colaId;

    public BasketChannelSwitchPayloadTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var pizza = await context.Products.FirstAsync(p => p.Name == "Test Pizza");
        var cola = await context.Products.FirstAsync(p => p.Name == "Test Cola");
        _pizzaId = pizza.Id;
        _colaId = cola.Id;

        // The pizza inherits "takeaway & delivery only" from its primary category, so switching the
        // basket to dine-in conflicts on it. The cola stays unrestricted, so a line SURVIVES the
        // switch — without one the assertions below would pass over an empty list.
        var mains = await context.Categories.FirstAsync(c => c.Name == "Main Course");
        mains.AvailableOrderTypes = TakeawayAndDelivery;

        context.ProductCategories.Add(new ProductCategory
        {
            ProductId = _pizzaId,
            CategoryId = mains.Id,
            IsPrimary = true,
            CreatedBy = "seed"
        });

        await context.SaveChangesAsync();
    }

    private async Task AddItemAsync(Guid productId, int quantity)
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(_sessionId, null,
            new AddToBasketDto { ProductId = productId, Quantity = quantity });
    }

    private async Task<Api.Features.Basket.Dtos.BasketChannelSwitchDto> SwitchToDineInAsync(bool removeConflicts)
    {
        using var scope = Factory.Services.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IBasketChannelService>();
        return await channelService.SetOrderTypeAsync(_sessionId, null, OrderType.DineIn, removeConflicts);
    }

    [Fact]
    public async Task BlockedSwitch_ReturnsTheBasketWithRealProductDetails_NotEmptyStrings()
    {
        await AddItemAsync(_pizzaId, 1);
        await AddItemAsync(_colaId, 2);

        var result = await SwitchToDineInAsync(removeConflicts: false);

        // Phase one changed nothing and reported the conflict.
        result.Applied.Should().BeFalse();
        result.Conflicts.Should().ContainSingle().Which.ProductName.Should().Be("Test Pizza");

        // The regression: the basket echoed back alongside those conflicts is what the client
        // re-renders its cart from, and every line of it used to come back nameless.
        result.Basket.Should().NotBeNull();
        result.Basket!.Items.Should().HaveCount(2);
        result.Basket.Items.Should().OnlyContain(i => !string.IsNullOrEmpty(i.ProductName),
            "a confirm dialog cannot ask a guest to approve removing items it cannot name");
        result.Basket.Items.Should().Contain(i => i.ProductId == _pizzaId && i.ProductName == "Test Pizza");
        result.Basket.Items.Should().Contain(i => i.ProductId == _colaId && i.ProductName == "Test Cola");

        // Description comes off the same unloaded navigation, so it fails and passes together with
        // the name — asserting it stops a fix that special-cases Name alone from looking complete.
        result.Basket.Items.Should().OnlyContain(i => !string.IsNullOrEmpty(i.ProductDescription));
    }

    // Attaches a bundle line — a parent row plus one child row — to the session's basket, the way a
    // ProductType.Menu combo lands in it. Written directly rather than through a MenuDefinition
    // because the mapper's ChildItems branch is what is under test, not the combo builder.
    private async Task AddBundleLineAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var basket = await context.Baskets.FirstAsync(b => b.SessionId == _sessionId);

        var parent = new BasketItem
        {
            BasketId = basket.Id,
            ProductId = _colaId,
            Quantity = 1,
            UnitPrice = 5m,
            ItemTotal = 5m,
            CreatedBy = "seed"
        };
        context.BasketItems.Add(parent);
        await context.SaveChangesAsync();

        context.BasketItems.Add(new BasketItem
        {
            BasketId = basket.Id,
            ProductId = _pizzaId,
            ParentBasketItemId = parent.Id,
            Quantity = 1,
            UnitPrice = 0m,
            ItemTotal = 0m,
            CreatedBy = "seed"
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task BlockedSwitch_NamesBundleChildLinesToo()
    {
        // Children degraded DIFFERENTLY from root lines and so need their own assertion: the mapper
        // falls back to string.Empty for a root name but not for a child's, so an unnamed child came
        // back as null. A root-only assertion passes over it — ChildItems is nested, and the DTO's
        // top-level Items list holds roots only.
        await AddItemAsync(_pizzaId, 1);
        await AddBundleLineAsync();

        var result = await SwitchToDineInAsync(removeConflicts: false);

        result.Applied.Should().BeFalse();
        result.Basket.Should().NotBeNull();

        var bundle = result.Basket!.Items.Should()
            .ContainSingle(i => i.ChildItems != null && i.ChildItems.Count > 0).Subject;
        bundle.ProductName.Should().Be("Test Cola");
        bundle.ChildItems!.Should().ContainSingle()
            .Which.ProductName.Should().Be("Test Pizza");
    }

    [Fact]
    public async Task BlockedSwitch_LeavesTheBasketUntouched()
    {
        await AddItemAsync(_pizzaId, 1);
        await AddItemAsync(_colaId, 2);

        await SwitchToDineInAsync(removeConflicts: false);

        using var verifyScope = Factory.Services.CreateScope();
        var context = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var basket = await context.Baskets.Include(b => b.Items).FirstAsync(b => b.SessionId == _sessionId);

        basket.OrderType.Should().BeNull("phase one must not apply the switch");
        basket.Items.Should().HaveCount(2, "phase one must not remove the conflicting line");
    }

    [Fact]
    public async Task ConfirmedSwitch_AppliesAndStillNamesEveryRemainingLine()
    {
        await AddItemAsync(_pizzaId, 1);
        await AddItemAsync(_colaId, 2);

        var result = await SwitchToDineInAsync(removeConflicts: true);

        result.Applied.Should().BeTrue();
        result.Removed.Should().ContainSingle().Which.ProductName.Should().Be("Test Pizza");
        result.Basket.Should().NotBeNull();
        result.Basket!.Items.Should().ContainSingle()
            .Which.ProductName.Should().Be("Test Cola");
    }
}
