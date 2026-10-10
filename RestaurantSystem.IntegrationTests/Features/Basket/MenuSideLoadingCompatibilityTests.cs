using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class MenuSideLoadingCompatibilityTests : IntegrationTestBase
{
    public MenuSideLoadingCompatibilityTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task BatchedSideLoad_PreservesFilteredIncludeBehaviorForDeletedRequiredSide()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var component = await context.Products.SingleAsync(product => product.Name == "Test Pizza");
        var sideProduct = await context.Products.SingleAsync(product => product.Name == "Test Cola");
        var membership = new ProductSideItem
        {
            Id = Guid.NewGuid(),
            MainProductId = component.Id,
            MainProduct = component,
            SideItemProductId = sideProduct.Id,
            SideItemProduct = sideProduct,
            IsRequired = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        sideProduct.IsDeleted = true;
        var menu = CreateTestMenu(component);
        CustomerStepManifestStore.Write(menu, new CustomerStepManifestDto
        {
            SchemaVersion = CustomerStepManifestStore.CurrentSchemaVersion,
            Revision = 0,
            Steps =
            [
                new CustomerStepManifestStepDto
                {
                    Kind = CustomerStepKind.BundleSection,
                    TargetId = menu.MenuDefinition!.Sections.Single().Id,
                    SectionId = menu.MenuDefinition.Sections.Single().Id,
                    CompositionRole = CompositionRole.Dish,
                    PresentationOrder = 0
                },
                new CustomerStepManifestStepDto
                {
                    Kind = CustomerStepKind.BundleComponentSide,
                    SectionItemId = menu.MenuDefinition.Sections.Single().Items.Single().Id,
                    ProductId = component.Id,
                    ScopeId = membership.Id,
                    CompositionRole = CompositionRole.Side,
                    PresentationOrder = 1
                }
            ]
        }, revision: 0);
        context.ProductSideItems.Add(membership);
        context.Products.Add(menu);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // This is the pre-batch Include shape used by bundle child loading. The required
        // SideItemProduct navigation must still obey Product's soft-delete query filter.
        var included = await context.Products.AsNoTracking()
            .Include(product => product.SuggestedSideItems)
                .ThenInclude(side => side.SideItemProduct)
            .SingleAsync(product => product.Id == component.Id);

        // Mirror the replacement's two-query loading shape in the same DbContext and snapshot.
        var batched = await context.Products.AsNoTracking()
            .Include(product => product.SuggestedSideItems)
            .SingleAsync(product => product.Id == component.Id);
        var sideIds = batched.SuggestedSideItems
            .Select(side => side.SideItemProductId)
            .Distinct()
            .ToList();
        var sideProducts = await context.Products.AsNoTracking()
            .Where(product => sideIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);
        foreach (var side in batched.SuggestedSideItems)
        {
            if (sideProducts.TryGetValue(side.SideItemProductId, out var loadedSide))
                side.SideItemProduct = loadedSide;
        }

        var includedMembershipIds = included.SuggestedSideItems.Select(side => side.Id).ToList();
        var batchedMembershipIds = batched.SuggestedSideItems.Select(side => side.Id).ToList();
        var includedFailure = ResolveRequiredSideSelection(included, membership.Id);
        var batchedFailure = ResolveRequiredSideSelection(batched, membership.Id);

        includedMembershipIds.Should().NotContain(membership.Id,
            "the old required-reference Include omits a membership whose side product is soft-deleted");
        batchedMembershipIds.Should().Contain(membership.Id,
            "the raw first half of a two-query load cannot apply the side product's global filter");
        includedFailure.Should().BeNull();
        batchedFailure.Should().Be<RestaurantSystem.Api.Common.Exceptions.BadRequestException>(
            "without pruning, the raw batch result would newly reject a bundle with a deleted required side");

        var menuForOrder = await context.Products
            .Include(product => product.MenuDefinition!)
                .ThenInclude(definition => definition.Sections)
                    .ThenInclude(section => section.Items)
            .SingleAsync(product => product.Id == menu.Id);
        var request = new AddToBasketDto
        {
            ProductId = menu.Id,
            Quantity = 1,
            SelectedMenuOptions =
            [
                new SelectedMenuOptionDto
                {
                    SectionId = menu.MenuDefinition!.Sections.Single().Id,
                    MenuSectionItemId = menu.MenuDefinition.Sections.Single().Items.Single().Id,
                    ItemId = component.Id,
                    Quantity = 1
                }
            ]
        };
        var factory = scope.ServiceProvider.GetRequiredService<IBasketItemFactory>();
        var basketId = Guid.NewGuid();
        var built = await factory.BuildMenuItemAsync(menuForOrder, request, basketId, null);
        built.ChildBasketItems.Should().ContainSingle();

        var basket = new RestaurantSystem.Domain.Entities.Basket
        {
            Id = basketId,
            SessionId = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        basket.Items.Add(built);
        context.Baskets.Add(basket);
        await context.SaveChangesAsync();

        (await context.BasketItems.AsNoTracking().CountAsync(item => item.BasketId == basketId))
            .Should().Be(2);
        var storedMembership = await context.ProductSideItems.AsNoTracking()
            .SingleAsync(row => row.Id == membership.Id);
        storedMembership.IsRequired.Should().BeTrue();
        storedMembership.SideItemProductId.Should().Be(sideProduct.Id);
    }

    private static Product CreateTestMenu(Product component)
    {
        var menu = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Deleted side query shape menu",
            BasePrice = 5m,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.Menu,
            Ingredients = [],
            Allergens = [],
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var definition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = menu.Id,
            Product = menu,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        var section = new MenuSection
        {
            Id = Guid.NewGuid(),
            MenuDefinitionId = definition.Id,
            MenuDefinition = definition,
            Name = "Main",
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        section.Items.Add(new MenuSectionItem
        {
            Id = Guid.NewGuid(),
            MenuSectionId = section.Id,
            MenuSection = section,
            ProductId = component.Id,
            Product = component,
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        definition.Sections.Add(section);
        menu.MenuDefinition = definition;
        return menu;
    }

    private static Type? ResolveRequiredSideSelection(Product component, Guid membershipId)
    {
        var step = new CustomerStepManifestStepDto
        {
            Kind = CustomerStepKind.BundleComponentSide,
            SectionItemId = Guid.NewGuid(),
            ProductId = component.Id,
            ScopeId = membershipId,
            CompositionRole = CompositionRole.Side,
            PresentationOrder = 1
        };
        try
        {
            BundleComponentSelection.ResolveSides(
                component,
                step.SectionItemId.GetValueOrDefault(),
                [],
                [step],
                orderType: null,
                maxQuantityPerItem: 100);
            return null;
        }
        catch (Exception exception)
        {
            return exception.GetType();
        }
    }
}
