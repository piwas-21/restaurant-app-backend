using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Services;
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

public sealed class CloseTableServiceSessionCommandHandler
    : ICommandHandler<CloseTableServiceSessionCommand, ApiResponse<TableServiceSessionDto>>
{
    private readonly decimal _paymentTolerance;
    private readonly ApplicationDbContext _context;
    private readonly ITableServiceSessionReader _reader;
    private readonly TimeProvider _timeProvider;

    public CloseTableServiceSessionCommandHandler(
        ApplicationDbContext context,
        ITableServiceSessionReader reader,
        TimeProvider? timeProvider = null,
        IOptions<TableServiceSessionSettings>? settings = null)
    {
        _context = context;
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
            var session = await TableServiceSessionRowLock.LoadAsync(
                _context, command.ServiceSessionId, cancellationToken);
            if (session is null)
            {
                return NotFound();
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

            // Legacy unassigned rounds are a separate visit and must be resolved explicitly.
            var legacyQuery = _context.Orders
                .AsQueryable();
            legacyQuery = TableServiceSessionCloseRules.ForUnassignedSession(
                legacyQuery, session.TableId, session.TableNumber);
            var legacyOrders = await legacyQuery
                .Where(order => !order.IsDeleted
                    && order.Type == OrderType.DineIn
                    && order.ServiceSessionId == null)
                .Select(order => new { order.Status, order.RemainingAmount })
                .ToListAsync(cancellationToken);
            var memberRows = await _context.Orders
                .Where(order => !order.IsDeleted && order.ServiceSessionId == session.Id)
                .Select(order => new { order.Status, order.RemainingAmount, order.OrderNumber })
                .ToListAsync(cancellationToken);
            var assessment = TableServiceSessionCloseRules.Assess(
                memberRows.Select(order =>
                    new TableServiceSessionOrderState(order.Status, order.RemainingAmount)),
                legacyOrders.Select(order =>
                    new TableServiceSessionOrderState(order.Status, order.RemainingAmount)),
                _paymentTolerance);
            if (assessment.LegacyActiveOrderCount > 0)
            {
                return Ambiguous();
            }

            var unresolved = memberRows
                .Where(order => TableServiceSessionCloseRules.IsUnresolvedMemberOrder(
                    new TableServiceSessionOrderState(order.Status, order.RemainingAmount)))
                .Select(order => order.OrderNumber)
                .ToList();
            if (assessment.Outstanding > _paymentTolerance || unresolved.Count > 0)
            {
                var errors = new List<string>();
                if (assessment.Outstanding > _paymentTolerance)
                {
                    errors.Add($"The session still has an outstanding balance of {assessment.Outstanding:0.00}.");
                }
                if (unresolved.Count > 0)
                {
                    errors.Add("The session still has unresolved rounds: " + string.Join(", ", unresolved));
                }
                return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                    errors, ErrorCodes.TableServiceSessionNotClosable);
            }

            session.Status = TableServiceSessionStatus.Closed;
            session.ClosedAt = now;
            session.Version++;
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

    private async Task<ApiResponse<TableServiceSessionDto>> ReadResultAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _reader.ReadAsync(id, cancellationToken);
        return result is null
            ? NotFound()
            : ApiResponse<TableServiceSessionDto>.SuccessWithData(result, "Table service session closed");
    }

    private static ApiResponse<TableServiceSessionDto> NotFound() =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            "Table service session was not found.", ErrorCodes.TableServiceSessionNotFound);

    private static ApiResponse<TableServiceSessionDto> Stale(int version) =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            $"The service session is stale; current version is {version}.",
            ErrorCodes.TableServiceSessionStale);

    private static ApiResponse<TableServiceSessionDto> Ambiguous() =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            TableBillTargetResolver.AmbiguousMessage, ErrorCodes.TableServiceSessionAmbiguous);
}
