using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

// Issue #158 (menu-bundles redesign slice 2): OrderItemDto.SideItems holds every child order item —
// a bundle/combo component AND a true add-on side item are otherwise indistinguishable, so
// MapToOrderItemDto stamps each child with a Kind. MapToOrderItemDto is a pure in-memory
// projection, so these drive the entity graph directly (no DB round-trip needed).
//
// WHAT THESE NOW COVER IS THE FALLBACK, not the primary path (#318). The Kind is persisted on the
// child row at write time; deriving it from the parent's product type was wrong, because
// Product.Type is mutable and retyping a product relabelled the children of orders already placed.
// These fixtures leave the child's Kind unset, which is exactly the shape of every order placed
// before that column existed — so they pin that historical orders keep rendering as they always
// did. The persisted path, and the precedence between the two, live in OrderChildQuantityTests.
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
        dto.SideItems!.Single().Kind.Should().Be(OrderItemKind.BundleChild);
    }

    [Fact]
    public void MapToOrderItemDto_RegularParent_StampsChildrenAsSideItem()
    {
        var dto = Map(ProductType.MainItem);

        dto.SideItems.Should().ContainSingle();
        dto.SideItems!.Single().Kind.Should().Be(OrderItemKind.SideItem);
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
