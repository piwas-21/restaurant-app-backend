using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// §9.3 — the add-to-basket guard only ever saw the LINE's product, so a channel-blocked product
/// could still enter the basket as a bundle option or as an add-on side item.
/// </summary>
/// <remarks>
/// <para>
/// Not a cosmetic gap: <c>OrderChannelGuard</c> already walks children at order creation, so the
/// basket could hold a line the order endpoint would later refuse — a dead end the guest discovers
/// at checkout, after choosing, instead of at add time.
/// </para>
/// <para>
/// Every product here <b>inherits</b> its channels from its primary category rather than carrying
/// its own mask. That is deliberate and is the point of the fixture: the guard resolves inheritance
/// through <c>ProductCategories → Category</c>, and those two queries loaded neither. A guard added
/// without widening them would resolve every inheriting option as UNRESTRICTED — permitting
/// everything while looking guarded.
/// </para>
/// </remarks>
public class BasketChannelGuardBundleTests : IntegrationTestBase
{
    public BasketChannelGuardBundleTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);

    private static readonly Guid ComboId = Guid.NewGuid();
    private static readonly Guid AllowedOptionId = Guid.NewGuid();
    private static readonly Guid BlockedOptionId = Guid.NewGuid();
    private static readonly Guid BlockedSideItemId = Guid.NewGuid();
    private static readonly Guid PlainProductId = Guid.NewGuid();
    private static readonly Guid SectionId = Guid.NewGuid();

    [Fact]
    public async Task BundleOption_BlockedOnTheBasketsChannel_IsRefused()
    {
        var act = () => BuildMenuAsync(OrderType.DineIn, BlockedOptionId);

        var thrown = await act.Should().ThrowAsync<BadRequestException>(
            "the combo is orderable on dine-in but the component chosen inside it is not");
        thrown.Which.ErrorCode.Should().Be(ErrorCodes.OrderTypeNotAvailable,
            "the client shows this message only for that code");
    }

    [Fact]
    public async Task BundleOption_AllowedOnTheBasketsChannel_IsAccepted()
    {
        var act = () => BuildMenuAsync(OrderType.DineIn, AllowedOptionId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BundleOption_WithNoChannelChosen_IsAccepted()
    {
        // The dominant browse state, and deliberately permissive: a null basket channel means the
        // guest has not picked yet, not that everything is refused.
        var act = () => BuildMenuAsync(null, BlockedOptionId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SideItem_BlockedOnTheBasketsChannel_IsRefused()
    {
        var act = () => BuildRegularAsync(OrderType.DineIn, BlockedSideItemId);

        var thrown = await act.Should().ThrowAsync<BadRequestException>(
            "an add-on side rides in under a product that IS allowed");
        thrown.Which.ErrorCode.Should().Be(ErrorCodes.OrderTypeNotAvailable);
    }

    private async Task BuildMenuAsync(OrderType? basketOrderType, Guid optionProductId)
    {
        using var scope = Factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IBasketItemFactory>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var combo = await LoadComboAsync(context);
        var request = new AddToBasketDto
        {
            ProductId = ComboId,
            Quantity = 1,
            SelectedMenuOptions =
            [
                new SelectedMenuOptionDto { SectionId = SectionId, ItemId = optionProductId, Quantity = 1 },
            ],
        };

        await factory.BuildMenuItemAsync(combo, request, Guid.NewGuid(), basketOrderType);
    }

    private async Task BuildRegularAsync(OrderType? basketOrderType, Guid sideItemId)
    {
        using var scope = Factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IBasketItemFactory>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var product = await context.Products.FindAsync(PlainProductId);
        var request = new AddToBasketDto
        {
            ProductId = PlainProductId,
            Quantity = 1,
            SelectedSideItems = [new SelectedSideItemDto { Id = sideItemId, Quantity = 1 }],
        };

        await factory.BuildRegularItemAsync(product!, null, request, Guid.NewGuid(), basketOrderType);
    }

    private static Task<Product> LoadComboAsync(ApplicationDbContext context) =>
        context.Products
            .Include(p => p.MenuDefinition!)
                .ThenInclude(d => d.Sections)
                    .ThenInclude(s => s.Items)
                        .ThenInclude(i => i.Product)
            .SingleAsync(p => p.Id == ComboId);

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var openCategory = new Category { Name = "§9.3 Unrestricted", AvailableOrderTypes = null, CreatedBy = "test" };
        var noDineInCategory = new Category
        {
            Name = "§9.3 Takeaway-Only",
            AvailableOrderTypes = TakeawayAndDelivery,
            CreatedBy = "test",
        };

        var combo = NewProduct(ComboId, "§9.3 Combo", ProductType.Menu, openCategory);
        var allowedOption = NewProduct(AllowedOptionId, "§9.3 Allowed Option", ProductType.MainItem, openCategory);
        var blockedOption = NewProduct(BlockedOptionId, "§9.3 Blocked Option", ProductType.MainItem, noDineInCategory);
        var blockedSide = NewProduct(BlockedSideItemId, "§9.3 Blocked Side", ProductType.AddOn, noDineInCategory);
        var plain = NewProduct(PlainProductId, "§9.3 Plain Product", ProductType.MainItem, openCategory);

        var definition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = combo.Id,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        };
        var section = new MenuSection
        {
            Id = SectionId,
            Name = "Choose one",
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        };
        section.Items.Add(NewSectionItem(allowedOption.Id));
        section.Items.Add(NewSectionItem(blockedOption.Id));
        definition.Sections.Add(section);

        context.AddRange(combo, allowedOption, blockedOption, blockedSide, plain);
        context.Add(definition);
        await context.SaveChangesAsync();
    }

    private static MenuSectionItem NewSectionItem(Guid productId) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = productId,
        AdditionalPrice = 0m,
        DisplayOrder = 0,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test",
    };

    /// <summary>
    /// Every fixture product INHERITS its channels — no product carries its own mask — so the tests
    /// fail unless the inheritance chain is actually loaded.
    /// </summary>
    private static Product NewProduct(Guid id, string name, ProductType type, Category category)
    {
        var product = new Product
        {
            Id = id,
            Name = name,
            BasePrice = 10m,
            IsActive = true,
            IsAvailable = true,
            Type = type,
            AvailableOrderTypes = null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        };
        product.ProductCategories.Add(new ProductCategory { Category = category, IsPrimary = true, CreatedBy = "test" });
        return product;
    }
}
