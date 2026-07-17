using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

// Issue #158 (menu-bundles redesign slice 2): OrderItemDto.SideItems holds every child
// order item — a bundle/combo component AND a true add-on side item are otherwise
// indistinguishable. MapToOrderItemDto now stamps each child with a Kind derived from the
// parent's product type (ProductType.Menu => BundleChild, otherwise SideItem), a DTO-only
// discriminator. MapToOrderItemDto is a pure in-memory projection, so these drive the
// entity graph directly (no DB round-trip needed).
public class OrderMappingServiceItemKindTests : IntegrationTestBase
{
    public OrderMappingServiceItemKindTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public void MapToOrderItemDto_BundleParent_StampsChildrenAsBundleChild()
    {
        var dto = Map(ProductType.Menu);

        dto.Kind.Should().BeNull("the top-level line item is neither a bundle child nor a side");
        dto.SideItems.Should().ContainSingle();
        dto.SideItems!.Single().Kind.Should().Be(ItemKind.BundleChild);
    }

    [Fact]
    public void MapToOrderItemDto_RegularParent_StampsChildrenAsSideItem()
    {
        var dto = Map(ProductType.MainItem);

        dto.SideItems.Should().ContainSingle();
        dto.SideItems!.Single().Kind.Should().Be(ItemKind.SideItem);
    }

    private OrderItemDto Map(ProductType parentType)
    {
        using var scope = Factory.Services.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IOrderMappingService>();
        return mapper.MapToOrderItemDto(BuildParentWithChild(parentType));
    }

    private static OrderItem BuildParentWithChild(ProductType parentType) => new()
    {
        Id = Guid.NewGuid(),
        ProductId = Guid.NewGuid(),
        ProductName = "Combo",
        Quantity = 1,
        UnitPrice = 10m,
        ItemTotal = 10m,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test",
        Product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Combo",
            Type = parentType,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        },
        ChildOrderItems = new List<OrderItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ProductId = Guid.NewGuid(),
                ProductName = "Coke",
                Quantity = 1,
                UnitPrice = 1.99m,
                ItemTotal = 1.99m,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test"
            }
        }
    };
}
