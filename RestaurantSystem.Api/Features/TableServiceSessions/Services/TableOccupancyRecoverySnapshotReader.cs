using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

internal sealed class TableOccupancyRecoverySnapshot
{
    public required Table Table { get; init; }
    public TableServiceSession? Session { get; init; }
    public required List<Order> Orders { get; init; }
    public required HashSet<Guid> LegacyOrderIds { get; init; }
    public required HashSet<Guid> AllocatedOrderIds { get; init; }
    public required HashSet<Guid> CheckoutAttemptOrderIds { get; init; }
    public required List<string> FinancialAttemptState { get; init; }
    public required int BlockingPaymentAttemptCount { get; init; }
    public required List<string> HandoffState { get; init; }
    public required List<Guid> AllocationIds { get; init; }
    public required string PreviewFingerprint { get; set; }
    public int ActivePaymentAttemptCount => BlockingPaymentAttemptCount;
    public int PendingPaymentHandoffCount => HandoffState.Count;
    public int CheckoutAttemptCount => CheckoutAttemptOrderIds.Count;

    public bool CanCancelUnsent(Order order) =>
        !AllocatedOrderIds.Contains(order.Id)
        && !CheckoutAttemptOrderIds.Contains(order.Id)
        && !(Session?.Id == order.ServiceSessionId
            && (ActivePaymentAttemptCount > 0 || PendingPaymentHandoffCount > 0))
        && order.Status is OrderStatus.Pending or OrderStatus.Confirmed
        && order.PaymentStatus == PaymentStatus.Pending
        && order.Payments.Count == 0
        && order.TotalPaid == 0
        && order.RoutingStates.Count == 0
        && !order.IsKitchenReleased
        && !order.KitchenReleasedAt.HasValue;

    public TableOccupancyRecoveryDispositionKind DispositionFor(Order order)
    {
        if (CanCancelUnsent(order))
            return TableOccupancyRecoveryDispositionKind.CancelledUnsent;
        if (LegacyOrderIds.Contains(order.Id))
            return TableOccupancyRecoveryDispositionKind.ArchivedLegacyOccupancy;
        return TableOccupancyRecoveryDispositionKind.RetainedInPriorVisit;
    }

    public TableOccupancyRecoveryPreviewDto ToPreview() => new(
        Table.Id,
        Table.TableNumber,
        Session?.Id,
        Session?.Version,
        Session?.AccountRevision,
        Table.ReadinessVersion,
        Session?.Currency,
        PreviewFingerprint,
        Orders.Count,
        Orders.Count(order => CanCancelUnsent(order)),
        LegacyOrderIds.Count,
        Orders.Count(IsRouted),
        Orders.Count(order => order.Status == OrderStatus.Preparing),
        Orders.Count(order => order.Status == OrderStatus.Ready),
        Orders.Count(IsFinanciallyTouched),
        ActivePaymentAttemptCount,
        PendingPaymentHandoffCount,
        CheckoutAttemptCount,
        Orders.Where(order => DispositionFor(order) != TableOccupancyRecoveryDispositionKind.CancelledUnsent)
            .Sum(TableServiceSessionCloseRules.Outstanding),
        Orders.Select(order => ToDto(order, LegacyOrderIds.Contains(order.Id), DispositionFor(order).ToString()))
            .ToArray());

    public static TableOccupancyRecoveryOrderDto ToDto(
        Order order, bool legacyUnassigned, string disposition) => new(
        order.Id,
        order.OrderNumber,
        disposition,
        order.Status.ToString(),
        order.PaymentStatus.ToString(),
        order.Total,
        order.BillingCreditAmount,
        order.TotalPaid,
        order.RemainingAmount,
        legacyUnassigned,
        order.IsKitchenReleased || order.KitchenReleasedAt.HasValue,
        order.RoutingStates.Count > 0);

    public static bool IsRouted(Order order) =>
        order.IsKitchenReleased || order.KitchenReleasedAt.HasValue || order.RoutingStates.Count > 0;

    public static bool IsFinanciallyTouched(Order order) =>
        order.PaymentStatus != PaymentStatus.Pending || order.Payments.Count > 0 || order.TotalPaid > 0;

    public static async Task<TableOccupancyRecoverySnapshot> ReadAsync(
        ApplicationDbContext context, Table table, TableServiceSession? session,
        decimal paymentTolerance,
        CancellationToken cancellationToken)
    {
        List<Order> memberOrders = session is null
            ? []
            : await context.Orders.Where(order => order.ServiceSessionId == session.Id)
                .Where(order => !order.IsDeleted)
                .Where(order => !context.TableOccupancyRecoveryDispositions.Any(disposition =>
                    disposition.TableId == table.Id && disposition.OrderId == order.Id))
                .Include(order => order.Payments)
                .Include(order => order.RoutingStates)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
        var tableNumber = TableReadinessLegacyRules.CanonicalNumber(table.TableNumber);
        var legacyOrders = await TableServiceSessionCloseRules.ForUnassignedSession(
                context.Orders,
                context.Set<TableOccupancyRecoveryDisposition>(),
                table.Id,
                tableNumber)
            .Where(order => !order.IsDeleted && order.Type == OrderType.DineIn
                && order.ServiceSessionId == null)
            .Where(TableServiceSessionCloseRules.BlockingLegacyQuery(paymentTolerance))
            .Include(order => order.Payments)
            .Include(order => order.RoutingStates)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var legacyIds = legacyOrders.Select(order => order.Id).ToHashSet();
        var orders = memberOrders.Concat(legacyOrders)
            .DistinctBy(order => order.Id)
            .OrderBy(order => order.CreatedAt)
            .ThenBy(order => order.Id)
            .ToList();
        var orderIds = orders.Select(order => order.Id).ToArray();
        List<Guid> allocatedIds = orderIds.Length == 0
            ? []
            : await context.AccountPaymentAllocations.AsNoTracking()
                .Where(value => orderIds.Contains(value.OrderId))
                .Select(value => value.OrderId)
                .Distinct()
                .ToListAsync(cancellationToken);
        List<Guid> checkoutAttemptOrderIds = orderIds.Length == 0
            ? []
            : await context.OrderCheckoutSessions.AsNoTracking()
                .Where(value => orderIds.Contains(value.OrderId))
                .Select(value => value.OrderId)
                .Distinct()
                .ToListAsync(cancellationToken);
        var attempts = new List<string>();
        var blockingPaymentAttemptCount = 0;
        if (session is not null)
        {
            var attemptRows = await context.AccountPaymentAttempts.AsNoTracking()
                .Where(value => value.ServiceSessionId == session.Id)
                .OrderBy(value => value.Id)
                .Select(value => new
                {
                    value.Id,
                    value.State,
                    value.AmountMinor,
                    value.Version,
                    value.UpdatedAt
                })
                .ToListAsync(cancellationToken);
            attempts = attemptRows.Select(value => string.Join("|", value.Id, value.State,
                value.AmountMinor, value.Version, value.UpdatedAt?.Ticks ?? 0)).ToList();
            blockingPaymentAttemptCount = attemptRows.Count(value => value.State.HoldsReservation());
        }
        var handoffs = new List<string>();
        if (session is not null)
        {
            var handoffRows = await context.TableServicePaymentHandoffs.AsNoTracking()
                .Where(value => value.ServiceSessionId == session.Id
                    && value.Status == TableServicePaymentHandoffStatus.Requested)
                .OrderBy(value => value.Id)
                .Select(value => new
                {
                    value.Id,
                    value.OperationId,
                    value.Status,
                    value.ExpectedVersion,
                    value.RequestedAmount,
                    value.RequestedAt
                })
                .ToListAsync(cancellationToken);
            handoffs = handoffRows.Select(value => string.Join("|", value.Id, value.OperationId,
                value.Status, value.ExpectedVersion, value.RequestedAmount,
                value.RequestedAt.Ticks)).ToList();
        }

        var snapshot = new TableOccupancyRecoverySnapshot
        {
            Table = table,
            Session = session,
            Orders = orders,
            LegacyOrderIds = legacyIds,
            AllocatedOrderIds = allocatedIds.ToHashSet(),
            CheckoutAttemptOrderIds = checkoutAttemptOrderIds.ToHashSet(),
            FinancialAttemptState = attempts,
            BlockingPaymentAttemptCount = blockingPaymentAttemptCount,
            HandoffState = handoffs,
            AllocationIds = allocatedIds.Order().ToList(),
            PreviewFingerprint = string.Empty
        };
        snapshot.PreviewFingerprint = Fingerprint(snapshot);
        return snapshot;
    }

    private static string Fingerprint(TableOccupancyRecoverySnapshot snapshot)
    {
        var builder = new StringBuilder();
        Append(builder, snapshot.Table.Id);
        Append(builder, snapshot.Table.ReadinessVersion);
        Append(builder, snapshot.Session?.Id);
        Append(builder, snapshot.Session?.Version);
        Append(builder, snapshot.Session?.AccountRevision);
        Append(builder, snapshot.Session?.Currency);
        foreach (var allocationId in snapshot.AllocationIds) Append(builder, allocationId);
        foreach (var orderId in snapshot.CheckoutAttemptOrderIds.Order()) Append(builder, orderId);
        foreach (var attempt in snapshot.FinancialAttemptState) Append(builder, attempt);
        foreach (var handoff in snapshot.HandoffState) Append(builder, handoff);
        foreach (var order in snapshot.Orders)
        {
            Append(builder, order.Id);
            Append(builder, order.OrderNumber);
            Append(builder, order.TableId);
            Append(builder, order.TableNumber);
            Append(builder, order.Type);
            Append(builder, order.ServiceSessionId);
            Append(builder, order.Status);
            Append(builder, order.PaymentStatus);
            Append(builder, order.Total.ToString(CultureInfo.InvariantCulture));
            Append(builder, order.BillingCreditAmount.ToString(CultureInfo.InvariantCulture));
            Append(builder, order.TotalPaid.ToString(CultureInfo.InvariantCulture));
            Append(builder, order.RemainingAmount.ToString(CultureInfo.InvariantCulture));
            Append(builder, order.IsKitchenReleased);
            Append(builder, order.KitchenReleasedAt?.Ticks);
            Append(builder, order.UpdatedAt?.Ticks);
            foreach (var payment in order.Payments.OrderBy(value => value.Id))
            {
                Append(builder, payment.Id);
                Append(builder, payment.PaymentMethod);
                Append(builder, payment.Status);
                Append(builder, payment.Amount.ToString(CultureInfo.InvariantCulture));
                Append(builder, payment.TipMinor);
                Append(builder, payment.TransactionId);
                Append(builder, payment.IsRefunded);
                Append(builder, payment.RefundedAmount?.ToString(CultureInfo.InvariantCulture));
                Append(builder, payment.RefundedTipMinor);
                Append(builder, payment.RefundDate?.Ticks);
            }
            foreach (var route in order.RoutingStates.OrderBy(value => value.JobId).ThenBy(value => value.Target))
            {
                Append(builder, route.JobId);
                Append(builder, route.Revision);
                Append(builder, route.Version);
                Append(builder, route.Target);
                Append(builder, route.Status);
                Append(builder, route.LastAcknowledgedAt?.Ticks);
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, object? value) =>
        builder.Append(value?.ToString() ?? string.Empty).Append('\0');
}
