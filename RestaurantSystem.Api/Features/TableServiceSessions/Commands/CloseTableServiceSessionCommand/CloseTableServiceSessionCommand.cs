using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.CloseTableServiceSessionCommand;

public record CloseTableServiceSessionCommand : ICommand<ApiResponse<TableServiceSessionDto>>
{
    [JsonIgnore]
    public Guid ServiceSessionId { get; set; }

    [JsonRequired]
    public int ExpectedVersion { get; set; }
}

public sealed partial class CloseTableServiceSessionCommandHandler
    : ICommandHandler<CloseTableServiceSessionCommand, ApiResponse<TableServiceSessionDto>>
{
    private readonly decimal _paymentTolerance;
    private readonly ApplicationDbContext _context;
    private readonly ITableServiceSessionReader _reader;
    private readonly TimeProvider _timeProvider;
    private readonly ITableGuestVisitRevoker _guestVisits;
    private readonly ITenantFeatures? _features;

    public CloseTableServiceSessionCommandHandler(
        ApplicationDbContext context,
        ITableServiceSessionReader reader,
        ITableGuestVisitRevoker guestVisits,
        TimeProvider? timeProvider = null,
        IOptions<TableServiceSessionSettings>? settings = null,
        ITenantFeatures? features = null)
    {
        _context = context;
        _guestVisits = guestVisits ?? throw new ArgumentNullException(nameof(guestVisits));
        _features = features;
        _reader = reader;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
    }

    public async Task<ApiResponse<TableServiceSessionDto>> Handle(
        CloseTableServiceSessionCommand command, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        // The explicit service-session row lock below is the serialization point shared with staff
        // round creation. Read committed is deliberate: a serializable snapshot taken before a
        // waiting create commits would still fail to see that committed order after the lock is
        // acquired and could close the visit incorrectly.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var lockedRows = await TableServiceSessionRowLock.LoadForLifecycleAsync(
                _context, command.ServiceSessionId, cancellationToken);
            var session = lockedRows.Session;
            if (session is null)
            {
                return NotFound();
            }

            if (lockedRows.IdentityChanged
                || session.TableId.HasValue && lockedRows.Table?.Id != session.TableId.Value)
            {
                return Stale(session.Version);
            }

            // Retrying a close after its commit is safe and does not require the client to retain the
            // pre-close version. A still-open session, however, always uses optimistic concurrency.
            if (session.Status == TableServiceSessionStatus.Closed)
            {
                await transaction.CommitAsync(cancellationToken);
                return await ReadResultAsync(session.Id, cancellationToken);
            }

            if (session.Version != command.ExpectedVersion)
            {
                return Stale(session.Version);
            }

            if (await TableServicePaymentHandoffRules.HasPendingAsync(
                _context, session.Id, cancellationToken))
            {
                return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                    "Resolve the pending cashier collection request before closing the session.",
                    ErrorCodes.TableServicePaymentHandoffPending);
            }

            await AccountPaymentCloseGuard.RequireClosableAsync(
                _context, session.Id, _features, cancellationToken);

            var kitchenCorrectionFailure = await CheckKitchenBoardCloseAsync(session, cancellationToken);
            if (kitchenCorrectionFailure is not null) return kitchenCorrectionFailure;

            var legacyQuery = TableServiceSessionCloseRules.ForUnassignedSession(
                _context.Orders, session.TableId, session.TableNumber);
            var legacyOrders = await legacyQuery
                .Where(order => !order.IsDeleted
                    && order.Type == OrderType.DineIn
                    && order.ServiceSessionId == null)
                .SelectCloseCharges()
                .ToListAsync(cancellationToken);
            var memberRows = await _context.Orders
                .Where(order => !order.IsDeleted && order.ServiceSessionId == session.Id)
                .SelectCloseCharges()
                .ToListAsync(cancellationToken);
            var memberStates = memberRows.Select(order => order.ToState()).ToList();
            var legacyStates = legacyOrders.Select(order => order.ToState()).ToList();
            var assessment = TableServiceSessionCloseRules.Assess(
                memberStates,
                legacyStates,
                _paymentTolerance);
            if (assessment.LegacyActiveOrderCount > 0)
            {
                return Ambiguous();
            }

            var unresolved = memberRows.Zip(memberStates)
                .Where(pair => TableServiceSessionCloseRules.IsUnresolvedMemberOrder(pair.Second))
                .Select(pair => pair.First.OrderNumber)
                .ToList();
            if (assessment.Outstanding > _paymentTolerance || unresolved.Count > 0)
            {
                return Unresolved(assessment.Outstanding, unresolved);
            }

            await _guestVisits.RevokeForSessionAsync(session.Id, now, cancellationToken);
            if (lockedRows.Table is not null)
            {
                lockedRows.Table.ReadinessState = TableReadinessState.NeedsReset;
                lockedRows.Table.ReadinessVersion++;
            }

            session.Status = TableServiceSessionStatus.Closed;
            session.ClosedAt = now;
            session.RecordAccountChange();
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await ReadResultAsync(session.Id, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolveConcurrencyAsync(command.ServiceSessionId, transaction, cancellationToken);
        }
        catch (Exception ex) when (PostgresConcurrencyAborts.IsMatch(ex, out _))
        {
            return await ResolveConcurrencyAsync(command.ServiceSessionId, transaction, cancellationToken);
        }
    }

    private async Task<ApiResponse<TableServiceSessionDto>> ResolveConcurrencyAsync(
        Guid serviceSessionId, IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        await ResetTransactionAsync(transaction, cancellationToken);
        var current = await _context.TableServiceSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == serviceSessionId, cancellationToken);
        if (current is null)
        {
            return NotFound();
        }

        // A concurrent close is an idempotent replay of the same final state. A payment or any
        // other versioned write leaves the visit open, so the caller must refresh before retrying.
        return current.Status == TableServiceSessionStatus.Closed
            ? await ReadResultAsync(current.Id, cancellationToken)
            : Stale(current.Version);
    }

    private async Task ResetTransactionAsync(
        IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            // A failed serializable statement aborts the PostgreSQL transaction. Detach it before
            // the reload below; otherwise EF may issue the read on the doomed transaction.
            await _context.Database.UseTransactionAsync(null, cancellationToken);
            _context.ChangeTracker.Clear();
        }
    }

}
