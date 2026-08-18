using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// §9.15 — the order-type SWITCH scan was root-only, so §9.3's add-time fix had an
/// add-then-switch twin.
/// </summary>
/// <remarks>
/// <para>
/// The sequence is the ORDINARY guest journey, not an edge case: browse with no channel chosen
/// (permissive by design, and the dominant browse state) → add a combo whose component is
/// takeaway-only → switch to dine-in → <b>zero conflicts reported</b>, no confirm dialog, channel
/// set → <c>OrderChannelGuard</c> flattens children at checkout and returns 400. The guest chose,
/// was told nothing, and was refused at the till.
/// </para>
/// <para>
/// Every fixture product <b>INHERITS</b> its channels from its primary category — none carries its
/// own mask. That is what makes these tests able to fail: <c>OrderTypeAvailability</c> resolves
/// inheritance through <c>ProductCategories → Category</c>, and an unloaded collection reads as
/// UNRESTRICTED rather than throwing. A scan widened to see more PRODUCTS but not more COLUMNS would
/// be permanently permissive and still look thorough, so dropping the include in
/// <c>BasketLineChannelScan.LoadProductsAsync</c> must turn these red on its own — verified
/// separately from the flattening itself.
/// </para>
/// </remarks>
[Collection("Database Lane 4")]
public class BasketChannelSwitchFlattensLinesTests : IntegrationTestBase
{
    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);

    private static readonly Guid ComboId = Guid.NewGuid();
    private static readonly Guid AllowedOptionId = Guid.NewGuid();
    private static readonly Guid BlockedOptionId = Guid.NewGuid();
    private static readonly Guid BlockedSideId = Guid.NewGuid();
    private static readonly Guid DineInOnlySideId = Guid.NewGuid();
    private static readonly Guid PlainId = Guid.NewGuid();
    private static readonly Guid SectionId = Guid.NewGuid();

    private readonly string _sessionId = Guid.NewGuid().ToString();

    public BasketChannelSwitchFlattensLinesTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>THE §9.15 case. Red before the fix: the scan never looked at the child row.</summary>
    [Fact]
    public async Task SwitchingChannel_ReportsALine_WhoseBUNDLECHILD_TheNewChannelForbids()
    {
        await AddComboAsync(BlockedOptionId);

        var result = await SwitchToDineInAsync(removeConflicts: false);

        result.Applied.Should().BeFalse("a conflict must block the switch until the guest confirms");
        result.Conflicts.Should().ContainSingle(
            "the combo itself is orderable on dine-in, but the component chosen inside it is not");
        result.Conflicts[0].ProductName.Should().Be("§9.15 Combo",
            "the LINE is what gets removed, so the line is what the guest is asked about");
    }

    /// <summary>The other half of the same gap: a side item rides in under an allowed product.</summary>
    [Fact]
    public async Task SwitchingChannel_ReportsALine_WhoseSIDEITEM_TheNewChannelForbids()
    {
        await AddPlainWithSideAsync(BlockedSideId);

        var result = await SwitchToDineInAsync(removeConflicts: false);

        result.Applied.Should().BeFalse();
        result.Conflicts.Should().ContainSingle(
            "the side lives in SelectedSideItemsJson, which the scan never read");
        result.Conflicts[0].ProductName.Should().Be("§9.15 Plain Product");
    }

    /// <summary>
    /// The reported reason is the INTERSECTION across the line, which is what
    /// <c>BasketChannelConflictDto.AllowedOrderTypes</c> already promises ("order types this line IS
    /// available on"). The combo inherits an unrestricted category, so a root-only reading would
    /// report all three order types — i.e. "removing this item, which is available for Dine-in".
    /// </summary>
    [Fact]
    public async Task TheReportedReason_DescribesTheWholeLine_NotJustItsRoot()
    {
        await AddComboAsync(BlockedOptionId);

        var result = await SwitchToDineInAsync(removeConflicts: false);

        result.Conflicts[0].AllowedOrderTypes.Should().BeEquivalentTo(
            new[] { OrderType.Takeaway, OrderType.Delivery },
            "the line is orderable only where every one of its parts is");
    }

    /// <summary>
    /// The fold must INTERSECT, not overwrite. Without this the last component's mask wins, and
    /// `combined = mask` — a plausible regression — passes every other test in this file, because no
    /// other fixture line carries two non-null masks.
    /// </summary>
    /// <remarks>
    /// The line here is orderable on NO channel: one side is takeaway+delivery, the other is
    /// dine-in, and those are disjoint — so the intersection is empty and every one of the three
    /// order types must conflict. Last-write-wins would report whichever side happened to be read
    /// last, let a switch to that channel apply with zero conflicts, and hand the guest a 400 at
    /// checkout from <c>OrderChannelGuard</c> — the §9.15 symptom exactly, one level of subtlety
    /// down. Running it as a Theory over all three channels is what makes ordering irrelevant.
    /// </remarks>
    [Theory]
    [InlineData(OrderType.DineIn)]
    [InlineData(OrderType.Takeaway)]
    [InlineData(OrderType.Delivery)]
    public async Task AlineWithDisjointComponentMasks_ConflictsOnEveryChannel(OrderType target)
    {
        await AddAsync(_sessionId, null, new AddToBasketDto
        {
            ProductId = PlainId,
            Quantity = 1,
            SelectedSideItems =
            [
                new SelectedSideItemDto { Id = BlockedSideId, Quantity = 1 },
                new SelectedSideItemDto { Id = DineInOnlySideId, Quantity = 1 },
            ],
        });

        using var scope = Factory.Services.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IBasketChannelService>();
        var result = await channelService.SetOrderTypeAsync(_sessionId, null, target, removeConflicts: false);

        result.Applied.Should().BeFalse("no channel satisfies both sides at once");
        result.Conflicts.Should().ContainSingle();
        result.Conflicts[0].AllowedOrderTypes.Should().BeEmpty(
            "takeaway+delivery and dine-in are disjoint, so the line has no orderable channel");
    }

    /// <summary>A combo whose chosen option IS allowed must not be dragged into the conflict list.</summary>
    [Fact]
    public async Task SwitchingChannel_LeavesALineAlone_WhenEveryComponentIsAllowed()
    {
        await AddComboAsync(AllowedOptionId);

        var result = await SwitchToDineInAsync(removeConflicts: false);

        result.Applied.Should().BeTrue("nothing on this line is restricted");
        result.Conflicts.Should().BeEmpty();
    }

    /// <summary>
    /// Confirming the removal must take the CHILD rows with the parent. The self-referencing FK has
    /// no cascade rule, so removing only parents would PROMOTE the children to top-level basket
    /// lines — and from there onto the kitchen ticket.
    /// </summary>
    [Fact]
    public async Task ConfirmingRemoval_DeletesTheChildRowsToo_NotJustTheParent()
    {
        await AddComboAsync(BlockedOptionId);

        var result = await SwitchToDineInAsync(removeConflicts: true);

        result.Applied.Should().BeTrue();
        result.Removed.Should().ContainSingle();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var surviving = await context.BasketItems
            .Where(i => i.Basket.SessionId == _sessionId)
            .ToListAsync();

        surviving.Should().BeEmpty("the parent and its bundle child both go, or the child is promoted");
    }

    /// <summary>
    /// The G11 twin. Merging re-homes rows by direct assignment and so bypasses the add-time guard;
    /// the merge check had the same root-only shape and so missed the same combos.
    /// </summary>
    [Fact]
    public async Task MergingAnonymousBasket_ClearsTheChannel_WhenAMergedLinesCHILDConflicts()
    {
        var userId = Guid.Parse(TestAuthHandler.UserId);

        // The user's basket is on dine-in and holds an unrestricted line.
        await AddAsync(null, userId, new AddToBasketDto { ProductId = PlainId, Quantity = 1 });
        using (var scope = Factory.Services.CreateScope())
        {
            var channelService = scope.ServiceProvider.GetRequiredService<IBasketChannelService>();
            await channelService.SetOrderTypeAsync(null!, userId, OrderType.DineIn, removeConflicts: true);
        }

        // The anonymous basket holds a combo whose component dine-in forbids.
        await AddComboAsync(BlockedOptionId);

        using (var scope = Factory.Services.CreateScope())
        {
            var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
            await basketService.MergeAnonymousBasketAsync(_sessionId, userId);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var merged = await context.Baskets.FirstAsync(b => b.UserId == userId && !b.IsDeleted);

            merged.OrderType.Should().BeNull(
                "never drop a line the guest chose and never keep an invalid line under a channel — "
                + "so the CHANNEL is cleared and the guest re-picks through the itemized switch");
        }
    }

    private async Task AddComboAsync(Guid optionProductId) =>
        await AddAsync(_sessionId, null, new AddToBasketDto
        {
            ProductId = ComboId,
            Quantity = 1,
            SelectedMenuOptions =
            [
                new SelectedMenuOptionDto { SectionId = SectionId, ItemId = optionProductId, Quantity = 1 },
            ],
        });

    private async Task AddPlainWithSideAsync(Guid sideItemId) =>
        await AddAsync(_sessionId, null, new AddToBasketDto
        {
            ProductId = PlainId,
            Quantity = 1,
            SelectedSideItems = [new SelectedSideItemDto { Id = sideItemId, Quantity = 1 }],
        });

    // Adds with the basket's channel still NULL — the permissive browse state the gap depends on.
    private async Task AddAsync(string? sessionId, Guid? userId, AddToBasketDto request)
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(sessionId!, userId, request);
    }

    private async Task<BasketChannelSwitchDto> SwitchToDineInAsync(bool removeConflicts)
    {
        using var scope = Factory.Services.CreateScope();
        var channelService = scope.ServiceProvider.GetRequiredService<IBasketChannelService>();
        return await channelService.SetOrderTypeAsync(_sessionId, null, OrderType.DineIn, removeConflicts);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var openCategory = new Category
        {
            Name = "§9.15 Unrestricted",
            AvailableOrderTypes = null,
            CreatedBy = "test",
        };
        var noDineInCategory = new Category
        {
            Name = "§9.15 Takeaway-Only",
            AvailableOrderTypes = TakeawayAndDelivery,
            CreatedBy = "test",
        };

        // Disjoint from noDineInCategory on purpose — a line carrying one side from each is
        // orderable on nothing, which is what pins the intersection.
        var dineInOnlyCategory = new Category
        {
            Name = "§9.15 Dine-in-Only",
            AvailableOrderTypes = (int)OrderChannels.DineIn,
            CreatedBy = "test",
        };

        var combo = NewProduct(ComboId, "§9.15 Combo", ProductType.Menu, openCategory);
        var allowedOption = NewProduct(AllowedOptionId, "§9.15 Allowed Option", ProductType.MainItem, openCategory);
        var blockedOption = NewProduct(BlockedOptionId, "§9.15 Blocked Option", ProductType.MainItem, noDineInCategory);
        var blockedSide = NewProduct(BlockedSideId, "§9.15 Blocked Side", ProductType.AddOn, noDineInCategory);
        var dineInOnlySide = NewProduct(DineInOnlySideId, "§9.15 Dine-in-Only Side", ProductType.AddOn, dineInOnlyCategory);
        var plain = NewProduct(PlainId, "§9.15 Plain Product", ProductType.MainItem, openCategory);

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

        context.AddRange(combo, allowedOption, blockedOption, blockedSide, dineInOnlySide, plain);
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
    /// Inherits its channels — <c>AvailableOrderTypes = null</c> — so the tests go red if the
    /// inheritance chain is not loaded. A product carrying its own mask would pass either way.
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
        product.ProductCategories.Add(new ProductCategory
        {
            Category = category,
            IsPrimary = true,
            CreatedBy = "test",
        });
        return product;
    }
}
