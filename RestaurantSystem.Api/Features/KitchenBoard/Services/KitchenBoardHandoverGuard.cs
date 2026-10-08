using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

public sealed record KitchenBoardHandoverState(
    bool Enabled,
    bool InitialWorkComplete,
    bool HasUnresolvedCorrections,
    bool CanResolveRoutingException)
{
    internal string? BlockReason(Order order)
    {
        if (!Enabled || !KitchenBoardWorkRules.IsActiveHandover(order)) return null;
        if (HasUnresolvedCorrections) return ErrorCodes.KitchenCorrectionUnresolved;
        if (!InitialWorkComplete && KitchenBoardWorkRules.HasOnlyNotConfiguredRoutes(order))
            return ErrorCodes.KitchenWorkUnresolved;
        return InitialWorkComplete
            && (!KitchenBoardWorkRules.HasRequiredRoutingException(order) || CanResolveRoutingException)
                ? null : ErrorCodes.RequiredRoutingUnresolved;
    }
}

internal static class KitchenBoardHandoverGuard
{
    internal static async Task<IReadOnlyDictionary<Guid, KitchenBoardHandoverState>> ReadAsync(
        ApplicationDbContext context,
        IReadOnlyCollection<Order> orders,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var ids = orders.Select(order => order.Id).Distinct().ToArray();
        if (!enabled || ids.Length == 0)
        {
            return orders.ToDictionary(order => order.Id,
                _ => new KitchenBoardHandoverState(false, true, false, false));
        }

        var completedIds = await context.KitchenBoardWorkCompletions.AsNoTracking()
            .Where(work => ids.Contains(work.OrderId) && work.WorkItemId == work.OrderId
                && work.Kind == KitchenBoardWorkKind.InitialOrder)
            .Select(work => work.OrderId)
            .ToHashSetAsync(cancellationToken);
        var printedReceipts = await context.DeviceOrderReceipts.AsNoTracking()
            .Where(receipt => ids.Contains(receipt.OrderId)
                && receipt.Status == DevicePrintStatus.Printed)
            .Select(receipt => new
            {
                receipt.OrderId,
                receipt.Target,
                receipt.DeviceId,
                receipt.JobId,
                receipt.Revision,
                receipt.JobType,
            })
            .ToListAsync(cancellationToken);
        var printedKeys = printedReceipts.Select(receipt => (
            receipt.OrderId,
            receipt.Target,
            receipt.DeviceId,
            receipt.JobId,
            receipt.Revision,
            receipt.JobType)).ToHashSet();
        var unresolvedOrderIds = await KitchenBoardWorkRules.ActiveCorrections(context)
            .Where(note => ids.Contains(note.OrderId))
            .Select(note => note.OrderId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        return orders.ToDictionary(order => order.Id, order =>
        {
            var baseWorkComplete = HasCompletedBaseWork(order, completedIds, printedKeys);
            return new KitchenBoardHandoverState(true, baseWorkComplete,
                unresolvedOrderIds.Contains(order.Id),
                KitchenBoardWorkRules.CanResolveRoutingExceptionWithBoard(order, baseWorkComplete));
        });
    }

    private static bool HasCompletedBaseWork(
        Order order,
        HashSet<Guid> completedIds,
        HashSet<(Guid OrderId, DevicePrintTarget Target, string DeviceId, Guid? JobId,
            int? Revision, DevicePrintJobType? JobType)> printedKeys)
    {
        if (!KitchenBoardWorkRules.IsActiveHandover(order)) return true;
        if (completedIds.Contains(order.Id) && KitchenBoardWorkRules.HasOnlyNotConfiguredRoutes(order))
            return true;

        var routes = order.RoutingStates.Where(state => state.IsRequired
            && state.Target != DevicePrintTarget.Cashier).ToList();
        return routes.Count > 0 && routes.All(route => route.DeviceId is not null
            && route.Status == DevicePrintStatus.Printed
            && HasMatchingOriginalReceipt(order.Id, route, printedKeys));
    }

    private static bool HasMatchingOriginalReceipt(
        Guid orderId,
        OrderRoutingState route,
        HashSet<(Guid OrderId, DevicePrintTarget Target, string DeviceId, Guid? JobId,
            int? Revision, DevicePrintJobType? JobType)> printedKeys)
    {
        if (route.DeviceId is null) return false;
        return printedKeys.Contains((orderId, route.Target, route.DeviceId,
                route.JobId, route.Revision, DevicePrintJobType.Order))
            || printedKeys.Contains((orderId, route.Target, route.DeviceId, null, null, null));
    }
}
