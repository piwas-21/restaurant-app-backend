using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal static class KitchenBoardCloseGuard
{
    internal static Task<bool> HasUnresolvedCorrectionAsync(
        ApplicationDbContext context,
        Guid serviceSessionId,
        Guid? tableId,
        int? tableNumber,
        CancellationToken cancellationToken)
    {
        var unassignedOrderIds = TableServiceSessionCloseRules.ForUnassignedSession(
                context.Orders, context.Set<TableOccupancyRecoveryDisposition>(), tableId, tableNumber)
            .Where(order => !order.IsDeleted && order.Type == OrderType.DineIn
                && order.ServiceSessionId == null)
            .Select(order => order.Id);
        var sessionOrderIds = context.Orders
            .Where(order => !order.IsDeleted && order.ServiceSessionId == serviceSessionId)
            .Select(order => order.Id);

        return KitchenBoardWorkRules.ActiveCorrections(context).AnyAsync(note =>
                sessionOrderIds.Contains(note.OrderId) || unassignedOrderIds.Contains(note.OrderId),
            cancellationToken);
    }
}
