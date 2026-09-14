using System.Data;
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
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var session = await _context.TableServiceSessions
                .SingleOrDefaultAsync(value => value.Id == command.ServiceSessionId, cancellationToken);
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

            // Explicit membership is immutable. An old anonymous order at this table is therefore
            // a second possible visit, not a round that may be silently closed with this session.
            if (await HasActiveUnassignedOrdersAsync(session.TableNumber, cancellationToken))
            {
                return Ambiguous();
            }

            var orders = await _context.Orders
                .Where(order => !order.IsDeleted && order.ServiceSessionId == session.Id)
                .Select(order => new { order.Status, order.RemainingAmount, order.OrderNumber })
                .ToListAsync(cancellationToken);
            var outstanding = orders
                .Where(order => order.Status != OrderStatus.Cancelled)
                .Sum(order => Math.Max(0, order.RemainingAmount));
            var unresolved = orders
                .Where(order => order.Status is not OrderStatus.Completed and not OrderStatus.Cancelled)
                .Select(order => order.OrderNumber)
                .ToList();
            if (outstanding > _paymentTolerance || unresolved.Count > 0)
            {
                var errors = new List<string>();
                if (outstanding > _paymentTolerance)
                {
                    errors.Add($"The session still has an outstanding balance of {outstanding:0.00}.");
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

    private async Task<bool> HasActiveUnassignedOrdersAsync(
        int tableNumber, CancellationToken cancellationToken) =>
        await _context.Orders.AnyAsync(order =>
            !order.IsDeleted
            && order.Type == OrderType.DineIn
            && order.TableNumber == tableNumber
            && order.ServiceSessionId == null
            && ((order.Status != OrderStatus.Completed && order.Status != OrderStatus.Cancelled)
                || (order.Status == OrderStatus.Completed && order.RemainingAmount > _paymentTolerance)),
            cancellationToken);

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
