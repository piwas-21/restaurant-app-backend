using System.Security.Cryptography;
using System.Text;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class OrderRoutingTargetResolver
{
    internal static HashSet<DevicePrintTarget> ResolveTargets(
        Order order, DeviceKitchenRoutingMode routingMode)
    {
        var targets = order.Items
            .Where(item => !item.ParentOrderItemId.HasValue)
            .SelectMany(item => ResolveItemTargets(item, routingMode, DevicePrintTarget.Default))
            .ToHashSet();
        // The cashier receipt is an independent operational destination. Keeping it in the
        // durable route set prevents a device-aware printer client from suppressing the cashier
        // copy merely because the kitchen also has a routed job.
        targets.Add(DevicePrintTarget.Cashier);
        return targets;
    }

    internal static IEnumerable<DevicePrintTarget> ResolveItemTargets(
        OrderItem item, DeviceKitchenRoutingMode routingMode,
        DevicePrintTarget inheritedTarget = DevicePrintTarget.Default)
    {
        var kitchenTypes = new List<KitchenType>();
        if (item.Product is not null)
        {
            kitchenTypes.Add(item.Product.KitchenType);
        }

        if (item.Menu?.MenuItems is not null)
        {
            kitchenTypes.AddRange(item.Menu.MenuItems
                .Select(menuItem => menuItem.Product)
                .OfType<Product>()
                .Select(product => product.KitchenType));
        }

        var declaredKitchenTypes = kitchenTypes
            .Where(kitchenType => kitchenType != KitchenType.None)
            .Distinct()
            .ToList();

        if (routingMode == DeviceKitchenRoutingMode.SingleKitchen)
        {
            return new[] { DevicePrintTarget.General }
                .Concat(item.ChildOrderItems.SelectMany(child =>
                    ResolveItemTargets(child, routingMode, DevicePrintTarget.General)));
        }

        var ownTarget = declaredKitchenTypes
            .Select(MapStationTarget)
            .FirstOrDefault(inheritedTarget);
        var childTargets = item.ChildOrderItems
            .SelectMany(child => ResolveItemTargets(child, routingMode, ownTarget))
            .ToList();

        // Order reads expose a root-only DTO tree, while release-side reads can contain the
        // tracked child rows as a navigation. Walk the complete tree so a nested bundle component
        // cannot disappear from its kitchen's route. A component without a kitchen inherits its
        // nearest parent's target in the printer app; it must not manufacture a General ticket here.
        if (declaredKitchenTypes.Count == 0 && childTargets.Count > 0)
        {
            return new[] { ownTarget }.Concat(childTargets);
        }

        if (declaredKitchenTypes.Count == 0)
        {
            return new[] { ownTarget }.Concat(childTargets);
        }

        return declaredKitchenTypes.Select(MapStationTarget)
            .Concat(childTargets);
    }

    private static DevicePrintTarget MapStationTarget(KitchenType kitchenType) => kitchenType switch
    {
        KitchenType.FrontKitchen => DevicePrintTarget.FrontKitchen,
        KitchenType.BackKitchen => DevicePrintTarget.BackKitchen,
        _ => DevicePrintTarget.Default,
    };

    internal static Guid CreateStableJobId(Guid orderId, DevicePrintTarget target)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"order-route:{orderId:N}:{target}"));
        return new Guid(bytes[..16]);
    }

    internal static bool CanApply(DevicePrintStatus current, DevicePrintStatus next)
    {
        if (current == next)
        {
            return true;
        }

        return current switch
        {
            DevicePrintStatus.Queued => next is DevicePrintStatus.Printed
                or DevicePrintStatus.Failed or DevicePrintStatus.Skipped or DevicePrintStatus.Unknown,
            DevicePrintStatus.Received or DevicePrintStatus.Sent or DevicePrintStatus.Unknown =>
                next is DevicePrintStatus.Printed or DevicePrintStatus.Failed or DevicePrintStatus.Unknown
                    or DevicePrintStatus.Sent or DevicePrintStatus.Skipped,
            DevicePrintStatus.NotConfigured => next is DevicePrintStatus.Queued
                or DevicePrintStatus.Skipped,
            _ => false
        };
    }
}
