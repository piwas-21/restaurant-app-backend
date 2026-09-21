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
            .SelectMany(item => ResolveItemTargets(item, routingMode))
            .ToHashSet();
        // The cashier receipt is an independent operational destination. Keeping it in the
        // durable route set prevents a device-aware printer client from suppressing the cashier
        // copy merely because the kitchen also has a routed job.
        targets.Add(DevicePrintTarget.Cashier);
        return targets;
    }

    internal static IEnumerable<DevicePrintTarget> ResolveItemTargets(
        OrderItem item, DeviceKitchenRoutingMode routingMode)
    {
        var kitchenTypes = new List<KitchenType>();
        if (item.Product is not null)
        {
            kitchenTypes.Add(item.Product.KitchenType);
        }

        if (item.Menu?.MenuItems is not null)
        {
            kitchenTypes.AddRange(item.Menu.MenuItems
                .Where(menuItem => menuItem.Product is not null)
                .Select(menuItem => menuItem.Product!.KitchenType));
        }

        var childTargets = item.ChildOrderItems
            .SelectMany(child => ResolveItemTargets(child, routingMode))
            .ToList();
        var declaredKitchenTypes = kitchenTypes
            .Where(kitchenType => kitchenType != KitchenType.None)
            .Distinct()
            .ToList();

        // Order reads expose a root-only DTO tree, while release-side reads can contain the
        // tracked child rows as a navigation. Walk the complete tree so a nested bundle component
        // cannot disappear from its kitchen's route. A component without a kitchen inherits its
        // parent's target in the printer app; it must not manufacture a General ticket here.
        if (declaredKitchenTypes.Count == 0 && childTargets.Count > 0)
        {
            return childTargets;
        }

        if (declaredKitchenTypes.Count == 0)
        {
            declaredKitchenTypes.Add(KitchenType.None);
        }

        return declaredKitchenTypes
            .Select(kitchenType => kitchenType switch
            {
                KitchenType.FrontKitchen when routingMode == DeviceKitchenRoutingMode.Stations
                    => DevicePrintTarget.FrontKitchen,
                KitchenType.BackKitchen when routingMode == DeviceKitchenRoutingMode.Stations
                    => DevicePrintTarget.BackKitchen,
                KitchenType.None when routingMode == DeviceKitchenRoutingMode.Stations
                    => DevicePrintTarget.Default,
                _ => DevicePrintTarget.General
            })
            .Concat(childTargets);
    }

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
            DevicePrintStatus.Queued => next is DevicePrintStatus.Received
                or DevicePrintStatus.Sent or DevicePrintStatus.Printed
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
