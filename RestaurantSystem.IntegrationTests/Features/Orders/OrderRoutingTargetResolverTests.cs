using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderRoutingTargetResolverTests
{
    [Fact]
    public void Stations_SplitsMixedRootAndChildLines()
    {
        var order = new Order
        {
            CreatedBy = "test",
            Items =
            [
                Item(KitchenType.FrontKitchen),
                Item(KitchenType.BackKitchen)
            ]
        };

        OrderRoutingTargetResolver.ResolveTargets(order, DeviceKitchenRoutingMode.Stations)
            .Should().BeEquivalentTo([DevicePrintTarget.Cashier, DevicePrintTarget.FrontKitchen,
                DevicePrintTarget.BackKitchen]);
    }

    [Fact]
    public void SingleKitchen_CollapsesMixedLinesToOneGeneralRoute()
    {
        var order = new Order
        {
            CreatedBy = "test",
            Items =
            [
                Item(KitchenType.FrontKitchen),
                Item(KitchenType.BackKitchen)
            ]
        };

        OrderRoutingTargetResolver.ResolveTargets(order, DeviceKitchenRoutingMode.SingleKitchen)
            .Should().BeEquivalentTo([DevicePrintTarget.Cashier, DevicePrintTarget.General]);
    }

    [Fact]
    public void MenuLine_IncludesBothMenuAndComponentKitchenTargets()
    {
        var menuComponent = new Product
        {
            KitchenType = KitchenType.BackKitchen,
            Name = "fries",
            CreatedBy = "test"
        };
        var order = new Order
        {
            CreatedBy = "test",
            Items =
            [
                new OrderItem
                {
                    Product = new Product
                    {
                        KitchenType = KitchenType.FrontKitchen,
                        Name = "combo",
                        CreatedBy = "test"
                    },
                    Menu = new Menu
                    {
                        CreatedBy = "test",
                        MenuItems =
                        [new MenuItem { Product = menuComponent, CreatedBy = "test" }]
                    },
                    ProductName = "combo",
                    Quantity = 1,
                    CreatedBy = "test"
                }
            ]
        };

        OrderRoutingTargetResolver.ResolveTargets(order, DeviceKitchenRoutingMode.Stations)
            .Should().BeEquivalentTo([DevicePrintTarget.Cashier, DevicePrintTarget.FrontKitchen,
                DevicePrintTarget.BackKitchen]);
    }

    [Fact]
    public void NestedChildLines_AreResolvedExactlyOnceFromRootTree()
    {
        var root = Item(KitchenType.FrontKitchen);
        var child = Item(KitchenType.None);
        var grandchild = Item(KitchenType.BackKitchen);
        child.ParentOrderItemId = root.Id = Guid.NewGuid();
        grandchild.ParentOrderItemId = child.Id = Guid.NewGuid();
        child.ChildOrderItems.Add(grandchild);
        root.ChildOrderItems.Add(child);

        var order = new Order { CreatedBy = "test", Items = [root, child, grandchild] };

        OrderRoutingTargetResolver.ResolveTargets(order, DeviceKitchenRoutingMode.Stations)
            .Should().BeEquivalentTo([DevicePrintTarget.Cashier, DevicePrintTarget.FrontKitchen,
                DevicePrintTarget.BackKitchen]);
    }

    [Fact]
    public void NoKitchenDesignation_UsesGeneralRoute()
    {
        var order = new Order { CreatedBy = "test", Items = [Item(KitchenType.None)] };

        OrderRoutingTargetResolver.ResolveTargets(order, DeviceKitchenRoutingMode.Stations)
            .Should().BeEquivalentTo([DevicePrintTarget.Cashier, DevicePrintTarget.Default]);

        OrderRoutingTargetResolver.ResolveTargets(order, DeviceKitchenRoutingMode.SingleKitchen)
            .Should().BeEquivalentTo([DevicePrintTarget.Cashier, DevicePrintTarget.General]);
    }

    [Fact]
    public void RouteJobIdentity_IsStablePerOrderAndTarget()
    {
        var orderId = Guid.NewGuid();

        OrderRoutingTargetResolver.CreateStableJobId(orderId, DevicePrintTarget.FrontKitchen)
            .Should().Be(OrderRoutingTargetResolver.CreateStableJobId(orderId, DevicePrintTarget.FrontKitchen));
        OrderRoutingTargetResolver.CreateStableJobId(orderId, DevicePrintTarget.FrontKitchen)
            .Should().NotBe(OrderRoutingTargetResolver.CreateStableJobId(orderId, DevicePrintTarget.BackKitchen));
    }

    [Fact]
    public void Acknowledgement_CannotMoveTerminalPrintedRouteBackToFailed()
    {
        OrderRoutingTargetResolver.CanApply(DevicePrintStatus.Printed, DevicePrintStatus.Failed)
            .Should().BeFalse();
        OrderRoutingTargetResolver.CanApply(DevicePrintStatus.Queued, DevicePrintStatus.Sent)
            .Should().BeTrue();
    }

    [Fact]
    public void RoutingStateVersion_IsAnOptimisticConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=unused;Database=unused")
            .Options;
        using var context = new ApplicationDbContext(options);

        context.Model.FindEntityType(typeof(OrderRoutingState))!
            .FindProperty(nameof(OrderRoutingState.Version))!
            .IsConcurrencyToken.Should().BeTrue();
    }

    private static OrderItem Item(KitchenType kitchenType) => new()
    {
        Product = new Product { KitchenType = kitchenType, Name = "test", CreatedBy = "test" },
        ProductName = "test",
        Quantity = 1,
        CreatedBy = "test"
    };
}
