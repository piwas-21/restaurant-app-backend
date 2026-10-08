using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal static class KitchenBoardWorkRules
{
    internal static IQueryable<OrderOperationalNote> ActiveCorrections(ApplicationDbContext context) =>
        context.OrderOperationalNotes.IgnoreQueryFilters().AsNoTracking()
            .Where(note => note.Audience == OrderNoteAudience.Kitchen
                && note.KitchenChangesJson != null && !note.WithdrawnAt.HasValue
                && note.Order.ExternalReference == null && !note.Order.IsDeleted
                && !context.KitchenBoardWorkCompletions.Any(work => work.OrderId == note.OrderId
                    && work.WorkItemId == note.Id
                    && work.Kind == KitchenBoardWorkKind.AmendmentCorrection)
                && !(note.KitchenTarget.HasValue && context.DeviceOrderReceipts.Any(receipt =>
                    receipt.OrderId == note.OrderId && receipt.JobId == note.Id
                    && receipt.JobType == DevicePrintJobType.Update
                    && receipt.Revision == PrinterUpdateRevisions.Original
                    && receipt.Target == note.KitchenTarget.Value
                    && receipt.Status == DevicePrintStatus.Printed
                    && note.Order.RoutingStates.Any(route => route.IsRequired
                        && route.Target == receipt.Target && route.DeviceId != null
                        && route.DeviceId == receipt.DeviceId))));

    internal static bool IsActiveHandover(Order order) =>
        order.IsKitchenReleased && order.ExternalReference is null
        && order.Status is OrderStatus.Ready or OrderStatus.OutForDelivery;

    internal static bool HasRequiredRoutingException(Order order) =>
        order.IsKitchenReleased && order.RoutingStates.Count == 0
        || order.RoutingStates.Any(state => state.IsRequired && IsRoutingException(state.Status));

    internal static bool IsRoutingException(DevicePrintStatus status) =>
        status is DevicePrintStatus.Failed or DevicePrintStatus.NotConfigured
            or DevicePrintStatus.Unknown or DevicePrintStatus.Skipped;

    internal static bool CanResolveRoutingExceptionWithBoard(Order order, bool initialWorkComplete)
    {
        if (!initialWorkComplete || !HasOnlyNotConfiguredRoutes(order)) return false;
        var exceptions = order.RoutingStates.Where(state => state.IsRequired
            && IsRoutingException(state.Status)).ToList();
        return exceptions.Count > 0 && exceptions.All(state => state.Target != DevicePrintTarget.Cashier
            && state.Status == DevicePrintStatus.NotConfigured && state.DeviceId is null);
    }

    internal static bool HasOnlyNotConfiguredRoutes(Order order)
    {
        var routes = order.RoutingStates.Where(state => state.IsRequired
            && state.Target != DevicePrintTarget.Cashier).ToList();
        return routes.Count > 0 && routes.All(state =>
            state.Status == DevicePrintStatus.NotConfigured && state.DeviceId is null);
    }
}
