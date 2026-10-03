using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

internal static class TableReadinessLegacyRules
{
    public static int? CanonicalNumber(string tableLabel) =>
        int.TryParse(tableLabel, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number > 0 && number.ToString(CultureInfo.InvariantCulture) == tableLabel
            ? number : null;

    public static async Task<bool> HasOpenVisitAsync(
        ApplicationDbContext context, Guid tableId, int? tableNumber, CancellationToken cancellationToken)
    {
        if (await HasStableOpenVisitAsync(context, tableId, cancellationToken))
        {
            return true;
        }

        return await HasAmbiguousLegacyOpenVisitAsync(context, tableNumber, cancellationToken);
    }

    public static Task<bool> HasStableOpenVisitAsync(
        ApplicationDbContext context, Guid tableId, CancellationToken cancellationToken) =>
        context.TableServiceSessions.AsNoTracking().AnyAsync(session =>
            session.Status == TableServiceSessionStatus.Open && session.TableId == tableId,
            cancellationToken);

    public static Task<bool> HasAmbiguousLegacyOpenVisitAsync(
        ApplicationDbContext context, int? tableNumber, CancellationToken cancellationToken) =>
        context.TableServiceSessions.AsNoTracking().AnyAsync(session =>
            session.Status == TableServiceSessionStatus.Open && session.TableId == null
            && (session.TableNumber == null || session.TableNumber == tableNumber),
            cancellationToken);

    public static async Task<bool> HasBlockingLegacyRoundAsync(
        ApplicationDbContext context,
        Guid tableId,
        int? tableNumber,
        decimal paymentTolerance,
        CancellationToken cancellationToken)
    {
        var unassigned = TableServiceSessionCloseRules.ForUnassignedSession(
            context.Orders.AsNoTracking(), tableId, tableNumber)
            .Where(order => !order.IsDeleted && order.Type == OrderType.DineIn
                && order.ServiceSessionId == null);
        var rows = await unassigned.Select(order => new
        {
            order.Status,
            order.RemainingAmount,
            IsFullyRefunded = order.PaymentStatus == PaymentStatus.Refunded
        }).ToListAsync(cancellationToken);
        if (rows.Any(order => IsBlocking(order.Status, order.RemainingAmount,
            order.IsFullyRefunded, paymentTolerance)))
        {
            return true;
        }

        var unidentified = await context.Orders.AsNoTracking().Where(order =>
            !order.IsDeleted && order.Type == OrderType.DineIn
            && order.ServiceSessionId == null && order.TableId == null && order.TableNumber == null)
            .Select(order => new
            {
                order.Status,
                order.RemainingAmount,
                IsFullyRefunded = order.PaymentStatus == PaymentStatus.Refunded
            }).ToListAsync(cancellationToken);
        return unidentified.Any(order => IsBlocking(order.Status, order.RemainingAmount,
            order.IsFullyRefunded, paymentTolerance));
    }

    private static bool IsBlocking(
        OrderStatus status, decimal remainingAmount, bool isFullyRefunded, decimal paymentTolerance) =>
        TableServiceSessionCloseRules.IsBlockingLegacyOrder(
            new TableServiceSessionOrderState(status, remainingAmount, isFullyRefunded), paymentTolerance);
}
