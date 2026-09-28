using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 4")]
public class NestedBasketLifecycleTests : IntegrationTestBase
{
    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);
    private static readonly Guid MenuId = Guid.NewGuid();
    private static readonly Guid ChildId = Guid.NewGuid();
    private static readonly Guid ChoiceId = Guid.NewGuid();
    private static readonly Guid PlainId = Guid.NewGuid();
    private static readonly Guid SectionId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid MembershipId = Guid.NewGuid();

    public NestedBasketLifecycleTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task QuantityUpdate_ScalesChildAndNestedChoiceWithoutChangingUnitPrice()
    {
        var session = Guid.NewGuid().ToString();
        await AddMenuAsync(session, null);
        var before = await ReadRowsAsync(session);
        var root = before.Single(row => row.ParentBasketItemId is null);
        var originalUnitPrice = root.UnitPrice;

        using (var scope = Factory.Services.CreateScope())
        {
            var basket = scope.ServiceProvider.GetRequiredService<IBasketService>();
            await basket.UpdateBasketItemAsync(session, root.Id, new UpdateBasketItemDto { Quantity = 2 });
        }

        var after = await ReadRowsAsync(session);
        after.Should().HaveCount(3);
        var updatedRoot = after.Single(row => row.Id == root.Id);
        var child = after.Single(row => row.ParentBasketItemId == root.Id);
        var choice = after.Single(row => row.ParentBasketItemId == child.Id);
        updatedRoot.Quantity.Should().Be(2);
        child.Quantity.Should().Be(2);
        choice.Quantity.Should().Be(2);
        updatedRoot.UnitPrice.Should().Be(originalUnitPrice);
        updatedRoot.ItemTotal.Should().Be(originalUnitPrice * 2);
        child.ItemTotal.Should().Be(0);
        choice.ItemTotal.Should().Be(0);
    }

    [Fact]
    public async Task RemovingMenuLine_RemovesItsGrandchildToo()
    {
        var session = Guid.NewGuid().ToString();
        await AddMenuAsync(session, null);
        var root = (await ReadRowsAsync(session)).Single(row => row.ParentBasketItemId is null);

        using (var scope = Factory.Services.CreateScope())
        {
            var basket = scope.ServiceProvider.GetRequiredService<IBasketService>();
            await basket.RemoveItemFromBasketAsync(session, root.Id);
        }

        (await ReadRowsAsync(session)).Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmingChannelConflict_RemovesTheWholeNestedLine()
    {
        var session = Guid.NewGuid().ToString();
        await AddMenuAsync(session, null);

        using (var scope = Factory.Services.CreateScope())
        {
            var channel = scope.ServiceProvider.GetRequiredService<IBasketChannelService>();
            var preview = await channel.SetOrderTypeAsync(session, null, OrderType.DineIn, removeConflicts: false);
            preview.Applied.Should().BeFalse();
            preview.Conflicts.Should().ContainSingle();
            var applied = await channel.SetOrderTypeAsync(session, null, OrderType.DineIn, removeConflicts: true);
            applied.Applied.Should().BeTrue();
            applied.Removed.Should().ContainSingle();
        }

        (await ReadRowsAsync(session)).Should().BeEmpty(
            "the nested ProductChoice may not be promoted into a standalone basket line");
    }

    [Fact]
    public async Task LoginMerge_RehomesEveryDescendantAndClearsConflictingChannel()
    {
        var session = Guid.NewGuid().ToString();
        var userId = Guid.Parse(TestAuthHandler.UserId);
        await AddPlainAsync(null, userId);
        using (var scope = Factory.Services.CreateScope())
        {
            var channel = scope.ServiceProvider.GetRequiredService<IBasketChannelService>();
            await channel.SetOrderTypeAsync(null!, userId, OrderType.DineIn, removeConflicts: true);
        }
        await AddMenuAsync(session, null);

        using (var scope = Factory.Services.CreateScope())
        {
            var basket = scope.ServiceProvider.GetRequiredService<IBasketService>();
            await basket.MergeAnonymousBasketAsync(session, userId);
        }

        using var readScope = Factory.Services.CreateScope();
        var context = readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userBasket = await context.Baskets.SingleAsync(b => b.UserId == userId && !b.IsDeleted);
        userBasket.OrderType.Should().BeNull("the nested choice excludes DineIn");
        var rows = await context.BasketItems.Where(row => row.BasketId == userBasket.Id).ToListAsync();
        rows.Should().HaveCount(4);
        var root = rows.Single(row => row.ProductId == MenuId);
        var child = rows.Single(row => row.ParentBasketItemId == root.Id);
        rows.Single(row => row.ParentBasketItemId == child.Id).ProductId.Should().Be(ChoiceId);
    }

    [Fact]
    public async Task LoginMergeOfSameBuild_ScalesNestedChoiceWithParent()
    {
        var session = Guid.NewGuid().ToString();
        var userId = Guid.Parse(TestAuthHandler.UserId);
        await AddMenuAsync(null, userId);
        await AddMenuAsync(session, null);

        using (var scope = Factory.Services.CreateScope())
        {
            var basket = scope.ServiceProvider.GetRequiredService<IBasketService>();
            await basket.MergeAnonymousBasketAsync(session, userId);
        }

        using var readScope = Factory.Services.CreateScope();
        var context = readScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userBasket = await context.Baskets.SingleAsync(b => b.UserId == userId && !b.IsDeleted);
        var rows = await context.BasketItems.Where(row => row.BasketId == userBasket.Id).ToListAsync();
        rows.Should().HaveCount(3, "identical menu builds should merge into one root");
        var root = rows.Single(row => row.ParentBasketItemId is null);
        var child = rows.Single(row => row.ParentBasketItemId == root.Id);
        var choice = rows.Single(row => row.ParentBasketItemId == child.Id);
        root.Quantity.Should().Be(2);
        child.Quantity.Should().Be(2);
        choice.Quantity.Should().Be(2);
        root.ItemTotal.Should().Be(root.UnitPrice * 2);
    }

    private async Task AddMenuAsync(string? session, Guid? userId)
    {
        using var scope = Factory.Services.CreateScope();
        var basket = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basket.AddItemToBasketAsync(session!, userId, new AddToBasketDto
        {
            ProductId = MenuId,
            Quantity = 1,
            SelectedMenuOptions =
            [
                new SelectedMenuOptionDto
                {
                    SectionId = SectionId,
                    ItemId = ChildId,
                    Quantity = 1,
                    CustomizationSelections =
                    [
                        new CustomizationGroupSelectionDto
                        {
                            GroupId = GroupId,
                            Options =
                            [
                                new CustomizationOptionSelectionDto
                                {
                                    Kind = CustomizationOptionKind.Product,
                                    OptionId = MembershipId
                                }
                            ]
                        }
                    ]
                }
            ]
        });
    }

    private async Task AddPlainAsync(string? session, Guid? userId)
    {
        using var scope = Factory.Services.CreateScope();
        var basket = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basket.AddItemToBasketAsync(session!, userId, new AddToBasketDto { ProductId = PlainId, Quantity = 1 });
    }

    private async Task<List<BasketItem>> ReadRowsAsync(string session)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.BasketItems.AsNoTracking()
            .Where(row => row.Basket.SessionId == session && !row.Basket.IsDeleted)
            .ToListAsync();
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var openCategory = new Category { Name = "Nested open", IsActive = true, CreatedBy = "test" };
        var restrictedCategory = new Category
        {
            Name = "Nested takeaway",
            IsActive = true,
            AvailableOrderTypes = TakeawayAndDelivery,
            CreatedBy = "test"
        };
        var menu = NewProduct(MenuId, "Nested menu", ProductType.Menu, openCategory);
        var child = NewProduct(ChildId, "Nested child", ProductType.MainItem, openCategory);
        var choice = NewProduct(ChoiceId, "Nested choice", ProductType.AddOn, restrictedCategory);
        var plain = NewProduct(PlainId, "Nested plain", ProductType.MainItem, openCategory);
        var group = new ProductCustomizationGroup
        {
            Id = GroupId,
            ProductId = child.Id,
            Name = "One choice",
            IsActive = true,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedBy = "test"
        };
        group.ProductOptions.Add(new ProductCustomizationProductOption
        {
            Id = MembershipId,
            OptionProductId = choice.Id,
            AdditionalPrice = 2m,
            CreatedBy = "test"
        });
        child.CustomizationGroups.Add(group);
        var definition = new MenuDefinition
        {
            ProductId = menu.Id,
            IsAlwaysAvailable = true,
            CreatedBy = "test"
        };
        var section = new MenuSection
        {
            Id = SectionId,
            Name = "Child",
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedBy = "test"
        };
        section.Items.Add(new MenuSectionItem
        {
            ProductId = child.Id,
            AdditionalPrice = 0m,
            CreatedBy = "test"
        });
        definition.Sections.Add(section);
        context.AddRange(menu, child, choice, plain, definition);
        await context.SaveChangesAsync();
    }

    private static Product NewProduct(Guid id, string name, ProductType type, Category category)
    {
        var product = new Product
        {
            Id = id,
            Name = name,
            BasePrice = 10m,
            Type = type,
            IsActive = true,
            IsAvailable = true,
            CreatedBy = "test"
        };
        product.ProductCategories.Add(new ProductCategory
        {
            Category = category,
            IsPrimary = true,
            CreatedBy = "test"
        });
        return product;
    }
}
