using System.Text.Json.Serialization;
using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
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
    private const decimal Tolerance = 0.01m;
    private readonly ApplicationDbContext _context;
    private readonly ITableServiceSessionReader _reader;
    private readonly TimeProvider _timeProvider;

    public CloseTableServiceSessionCommandHandler(
        ApplicationDbContext context, ITableServiceSessionReader reader,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _reader = reader;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<TableServiceSessionDto>> Handle(
        CloseTableServiceSessionCommand command, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
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
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                $"The service session is stale; current version is {session.Version}.",
                ErrorCodes.TableServiceSessionStale);
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
        if (outstanding > Tolerance || unresolved.Count > 0)
        {
            var errors = new List<string>();
            if (outstanding > Tolerance)
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
}
