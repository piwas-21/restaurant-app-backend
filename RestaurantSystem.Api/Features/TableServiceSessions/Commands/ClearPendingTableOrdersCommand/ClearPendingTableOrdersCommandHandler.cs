using System.Data;
using System.Globalization;
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
    private const string ClearReason = "Pending table order cleared by staff before kitchen routing.";

    public async Task<ApiResponse<ClearedTableOrdersDto>> Handle(
        ClearPendingTableOrdersCommand command, CancellationToken cancellationToken)
    {
        if (command.ServiceSessionId.HasValue == command.TableNumber.HasValue
            || command.TableNumber is <= 0
            || command.ServiceSessionId == Guid.Empty)
            return Refused("Select exactly one valid table visit or legacy table number.");

        await using var transaction = await context.Database.BeginTransactionAsync(
            command.ServiceSessionId.HasValue ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable,
            cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        TableServiceSession? session = null;
        Table? table = null;
        IQueryable<Order> orderQuery;
        if (command.ServiceSessionId is Guid sessionId)
        {
            var locked = await TableServiceSessionRowLock.LoadForLifecycleAsync(context, sessionId, cancellationToken);
            session = locked.Session;
            if (session is null) return NotFound();
            if (locked.IdentityChanged || session.TableId.HasValue && locked.Table?.Id != session.TableId.Value)
                return Stale(session.Version);
            if (session.Status != TableServiceSessionStatus.Open)
                return Refused("A closed table visit cannot clear pending orders.");
            if (command.ExpectedVersion is null || command.ExpectedVersion != session.Version)
                return Stale(session.Version);
            if (await TableServicePaymentHandoffRules.HasPendingAsync(context, session.Id, cancellationToken))
                return Refused("Resolve the pending cashier collection request before clearing this table.");
            if (await context.AccountPaymentAttempts.AnyAsync(value => value.ServiceSessionId == session.Id
                    && value.State != AccountPaymentState.Failed && value.State != AccountPaymentState.Released,
                    cancellationToken))
                return Refused("Resolve all account payment attempts before clearing this table.");
            table = locked.Table;
            orderQuery = context.Orders.Where(value => value.ServiceSessionId == session.Id);
        }
        else
        {
            var tableNumber = command.TableNumber!.Value;
            if (await context.TableServiceSessions.AnyAsync(value => value.TableNumber == tableNumber
                    && value.Status == TableServiceSessionStatus.Open, cancellationToken))
                return Refused("Use the explicit visit instead of the legacy table-number action.");
            orderQuery = context.Orders.Where(value => value.TableNumber == tableNumber
                && value.ServiceSessionId == null && value.Type == OrderType.DineIn);
            table = await context.Tables.FirstOrDefaultAsync(value =>
                    value.TableNumber == tableNumber.ToString(CultureInfo.InvariantCulture),
                cancellationToken);
        }

        var orders = await orderQuery.Where(value => !value.IsDeleted && value.Status != OrderStatus.Cancelled)
            .Include(value => value.Payments)
            .Include(value => value.RoutingStates)
            .ToListAsync(cancellationToken);
        if (orders.Any(value => value.Status != OrderStatus.Pending || value.Payments.Count > 0
                || value.TotalPaid > 0 || value.RoutingStates.Count > 0))
            return Refused("Only unprinted pending orders without payments or routing history can be cleared.");

        var audit = currentUser.GetAuditIdentifier();
        foreach (var order in orders)
        {
            order.Status = OrderStatus.Cancelled;
            order.CancellationReason = ClearReason;
            order.UpdatedAt = now;
            order.UpdatedBy = audit;
            context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                FromStatus = OrderStatus.Pending,
                ToStatus = OrderStatus.Cancelled,
                Notes = ClearReason,
                ChangedAt = now,
                ChangedBy = audit,
                CreatedBy = audit
            });
        }

        DateTime? releasedAt = session?.ReleasedAt;
        if (session is not null)
        {
            if (!session.ReleasedAt.HasValue)
            {
                await guestVisits.RevokeForSessionAsync(session.Id, now, cancellationToken);
                session.ReleasedAt = now;
                session.ReleasedBy = audit;
                releasedAt = now;
            }
            session.RecordAccountChange();
        }
        if (table is not null)
        {
            table.ReadinessState = TableReadinessState.NeedsReset;
            table.ReadinessVersion++;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ApiResponse<ClearedTableOrdersDto>.SuccessWithData(new ClearedTableOrdersDto(
            session?.Id, command.TableNumber ?? session?.TableNumber, orders.Count, now, releasedAt),
            "Pending table orders cleared without dispatching them.");
    }

    private static ApiResponse<ClearedTableOrdersDto> NotFound() =>
        ApiResponse<ClearedTableOrdersDto>.FailureWithCode(
            "Table service session was not found.", ErrorCodes.TableServiceSessionNotFound);

    private static ApiResponse<ClearedTableOrdersDto> Stale(int version) =>
        ApiResponse<ClearedTableOrdersDto>.FailureWithCode(
            $"The service session is stale; current version is {version}.", ErrorCodes.TableServiceSessionStale);

    private static ApiResponse<ClearedTableOrdersDto> Refused(string message) =>
        ApiResponse<ClearedTableOrdersDto>.FailureWithCode(message, ErrorCodes.TableServiceSessionNotClosable);
}
