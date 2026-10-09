using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.ClearPendingTableOrdersCommand;

public sealed class ClearPendingTableOrdersCommandHandler(
    ApplicationDbContext context,
    ICurrentUserService currentUser,
    ITableGuestVisitRevoker guestVisits,
    TimeProvider timeProvider)
    : ICommandHandler<ClearPendingTableOrdersCommand, ApiResponse<ClearedTableOrdersDto>>
{
    private const string ClearReason = "Unrouted table order cleared by staff before kitchen routing.";

    public async Task<ApiResponse<ClearedTableOrdersDto>> Handle(
        ClearPendingTableOrdersCommand command, CancellationToken cancellationToken)
    {
        if (!HasOneValidTarget(command))
            return Refused("Select exactly one valid table visit or legacy table number.");

        await using var transaction = await context.Database.BeginTransactionAsync(
            command.ServiceSessionId.HasValue ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable,
            cancellationToken);
        var targetResult = await ResolveTargetAsync(command, cancellationToken);
        if (targetResult.Error is not null) return targetResult.Error;

        var target = targetResult.Target!;
        var orders = await LoadOrdersAsync(target, cancellationToken);
        if (orders.Any(IsProtectedOrder))
            return Refused("Only unpaid pending or held-confirmed orders without kitchen release or routing history can be cleared.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var audit = currentUser.GetAuditIdentifier();
        CancelOrders(orders, now, audit);
        var releasedAt = await ReleaseSessionAsync(target.Session, now, audit, cancellationToken);
        ResetTable(target.Table);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ApiResponse<ClearedTableOrdersDto>.SuccessWithData(new ClearedTableOrdersDto(
            target.Session?.Id, command.TableNumber ?? target.Session?.TableNumber, orders.Count, now, releasedAt),
            "Unrouted table orders cleared without dispatching them.");
    }

    private async Task<TargetResolution> ResolveTargetAsync(
        ClearPendingTableOrdersCommand command, CancellationToken cancellationToken) =>
        command.ServiceSessionId is Guid sessionId
            ? await ResolveSessionTargetAsync(sessionId, command.ExpectedVersion, cancellationToken)
            : await ResolveLegacyTargetAsync(command.TableNumber!.Value, cancellationToken);

    private async Task<TargetResolution> ResolveSessionTargetAsync(
        Guid sessionId, int? expectedVersion, CancellationToken cancellationToken)
    {
        var locked = await TableServiceSessionRowLock.LoadForLifecycleAsync(context, sessionId, cancellationToken);
        var session = locked.Session;
        if (session is null) return TargetResolution.Failed(NotFound());
        if (locked.IdentityChanged || (session.TableId.HasValue && locked.Table?.Id != session.TableId.Value))
            return TargetResolution.Failed(Stale(session.Version));
        if (!TableServiceSessionRowLock.IsPhysicalTableResolved(session, locked.Table))
            return TargetResolution.Failed(Refused("Resolve the physical table identity before clearing pending orders."));
        if (session.Status != TableServiceSessionStatus.Open)
            return TargetResolution.Failed(Refused("A closed table visit cannot clear pending orders."));
        if (expectedVersion is null || expectedVersion != session.Version)
            return TargetResolution.Failed(Stale(session.Version));
        if (await TableServicePaymentHandoffRules.HasPendingAsync(context, session.Id, cancellationToken))
            return TargetResolution.Failed(Refused(
                "Resolve the pending cashier collection request before clearing this table."));
        if (await HasActivePaymentAttemptAsync(session.Id, cancellationToken))
            return TargetResolution.Failed(Refused("Resolve all account payment attempts before clearing this table."));

        return TargetResolution.Resolved(new ClearTarget(
            session, locked.Table, context.Orders.Where(order => order.ServiceSessionId == session.Id)));
    }

    private async Task<TargetResolution> ResolveLegacyTargetAsync(
        int tableNumber, CancellationToken cancellationToken)
    {
        var table = await TableServiceSessionRowLock.LoadTableByNumberAsync(context, tableNumber, cancellationToken);
        if (table is null)
            return TargetResolution.Failed(Refused("Resolve the legacy table identity before clearing its pending orders."));
        if (await HasOpenVisitForTableAsync(tableNumber, cancellationToken))
            return TargetResolution.Failed(Refused("Use the explicit visit instead of the legacy table-number action."));

        var orders = context.Orders.Where(order => order.TableNumber == tableNumber
            && order.ServiceSessionId == null && order.Type == OrderType.DineIn);
        return TargetResolution.Resolved(new ClearTarget(null, table, orders));
    }

    private async Task<bool> HasActivePaymentAttemptAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await context.AccountPaymentAttempts.AnyAsync(value => value.ServiceSessionId == sessionId
            && value.State != AccountPaymentState.Failed && value.State != AccountPaymentState.Released,
            cancellationToken);

    private async Task<bool> HasOpenVisitForTableAsync(int tableNumber, CancellationToken cancellationToken) =>
        await context.TableServiceSessions.AnyAsync(value => value.TableNumber == tableNumber
            && value.Status == TableServiceSessionStatus.Open, cancellationToken);

    private static async Task<List<Order>> LoadOrdersAsync(ClearTarget target, CancellationToken cancellationToken) =>
        await target.Orders.Where(order => !order.IsDeleted && order.Status != OrderStatus.Cancelled)
            .Include(order => order.Payments)
            .Include(order => order.RoutingStates)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    private static bool IsProtectedOrder(Order order) =>
        !CanCancelBeforeKitchenRelease(order.Status) || order.PaymentStatus != PaymentStatus.Pending
        || order.Payments.Count > 0 || order.TotalPaid > 0
        || order.RoutingStates.Count > 0 || order.IsKitchenReleased || order.KitchenReleasedAt.HasValue;

    private static bool CanCancelBeforeKitchenRelease(OrderStatus status) =>
        status is OrderStatus.Pending or OrderStatus.Confirmed;

    private void CancelOrders(IEnumerable<Order> orders, DateTime now, string audit)
    {
        foreach (var order in orders)
        {
            var previousStatus = order.Status;
            order.Status = OrderStatus.Cancelled;
            order.CancellationReason = ClearReason;
            order.UpdatedAt = now;
            order.UpdatedBy = audit;
            context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                FromStatus = previousStatus,
                ToStatus = OrderStatus.Cancelled,
                Notes = ClearReason,
                ChangedAt = now,
                ChangedBy = audit,
                CreatedBy = audit
            });
        }
    }

    private async Task<DateTime?> ReleaseSessionAsync(
        TableServiceSession? session, DateTime now, string audit, CancellationToken cancellationToken)
    {
        if (session is null) return null;
        var releasedAt = session.ReleasedAt;
        if (!releasedAt.HasValue)
        {
            await guestVisits.RevokeForSessionAsync(session.Id, now, cancellationToken);
            session.ReleasedAt = now;
            session.ReleasedBy = audit;
            releasedAt = now;
        }
        session.RecordAccountChange();
        return releasedAt;
    }

    private static void ResetTable(Table? table)
    {
        if (table is null) return;
        table.ReadinessState = TableReadinessState.NeedsReset;
        table.ReadinessVersion++;
    }

    private static bool HasOneValidTarget(ClearPendingTableOrdersCommand command) =>
        command.ServiceSessionId.HasValue != command.TableNumber.HasValue
        && (command.TableNumber is null or > 0)
        && command.ServiceSessionId != Guid.Empty;

    private static ApiResponse<ClearedTableOrdersDto> NotFound() =>
        ApiResponse<ClearedTableOrdersDto>.FailureWithCode(
            "Table service session was not found.", ErrorCodes.TableServiceSessionNotFound);

    private static ApiResponse<ClearedTableOrdersDto> Stale(int version) =>
        ApiResponse<ClearedTableOrdersDto>.FailureWithCode(
            $"The service session is stale; current version is {version}.", ErrorCodes.TableServiceSessionStale);

    private static ApiResponse<ClearedTableOrdersDto> Refused(string message) =>
        ApiResponse<ClearedTableOrdersDto>.FailureWithCode(message, ErrorCodes.TableServiceSessionNotClosable);

    private sealed record ClearTarget(TableServiceSession? Session, Table? Table, IQueryable<Order> Orders);

    private sealed record TargetResolution(ClearTarget? Target, ApiResponse<ClearedTableOrdersDto>? Error)
    {
        public static TargetResolution Resolved(ClearTarget target) => new(target, null);
        public static TargetResolution Failed(ApiResponse<ClearedTableOrdersDto> error) => new(null, error);
    }
}
