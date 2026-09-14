using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.AddTableServiceSessionPaymentCommand;

public sealed class AddTableServiceSessionPaymentCommandHandler
    : ICommandHandler<AddTableServiceSessionPaymentCommand, ApiResponse<TableServiceSessionDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ITableServiceSessionPaymentWriter _writer;
    private readonly ITableServiceSessionPaymentReplayResolver _replays;
    private readonly ITableServiceSessionReader _reader;
    private readonly ILogger<AddTableServiceSessionPaymentCommandHandler> _logger;

    public AddTableServiceSessionPaymentCommandHandler(
        ApplicationDbContext context,
        ITableServiceSessionPaymentWriter writer,
        ITableServiceSessionPaymentReplayResolver replays,
        ITableServiceSessionReader reader,
        ILogger<AddTableServiceSessionPaymentCommandHandler> logger)
    {
        _context = context;
        _writer = writer;
        _replays = replays;
        _reader = reader;
        _logger = logger;
    }

    public async Task<ApiResponse<TableServiceSessionDto>> Handle(
        AddTableServiceSessionPaymentCommand command, CancellationToken cancellationToken)
    {
        command.Currency = CurrencyCode.Normalize(command.Currency);
        var replay = await _replays.ResolveAsync(command, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

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

            if (session.Status != TableServiceSessionStatus.Open)
            {
                return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                    "A closed table service session cannot accept another payment.",
                    ErrorCodes.TableServiceSessionNotClosable);
            }

            if (session.Version != command.ExpectedVersion)
            {
                return Stale(session.Version);
            }

            var write = await _writer.ApplyAsync(session, command, cancellationToken);
            if (!write.Success)
            {
                return write.CurrencyMismatch
                    ? ApiResponse<TableServiceSessionDto>.FailureWithCode(
                        write.Error!, ErrorCodes.TableServiceSessionCurrencyMismatch)
                    : ApiResponse<TableServiceSessionDto>.Failure(write.Error!);
            }

            session.Version++;
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var response = await _reader.ReadAsync(session.Id, cancellationToken);
            return response is null
                ? NotFound()
                : ApiResponse<TableServiceSessionDto>.SuccessWithData(
                    response,
                    $"Payment of {write.Applied:0.00} applied across {write.OrderCount} order(s)");
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolveConcurrencyAsync(command, transaction, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsOperationConflict(ex))
        {
            await ResetTransactionAsync(transaction, cancellationToken);
            var winner = await _replays.ResolveAsync(command, cancellationToken);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }
        catch (Exception ex) when (PostgresConcurrencyAborts.IsMatch(ex, out var sqlState))
        {
            _logger.LogWarning(ex, "Table service session payment lost a concurrency race: {SqlState}", sqlState);
            return await ResolveConcurrencyAsync(command, transaction, cancellationToken);
        }
    }

    private async Task<ApiResponse<TableServiceSessionDto>> ResolveConcurrencyAsync(
        AddTableServiceSessionPaymentCommand command,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ResetTransactionAsync(transaction, cancellationToken);

        // The operation row is the durable replay anchor. If the commit outcome was uncertain,
        // replaying the exact payload is safe; otherwise the fresh session version is the stable
        // optimistic-concurrency answer for the caller.
        var replay = await _replays.ResolveAsync(command, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var current = await _context.TableServiceSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == command.ServiceSessionId, cancellationToken);
        return current is null ? NotFound() : Stale(current.Version);
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
            // replay/reload queries; otherwise EF may issue those reads on the doomed transaction.
            await _context.Database.UseTransactionAsync(null, cancellationToken);
            _context.ChangeTracker.Clear();
        }
    }

    private static bool IsOperationConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName?.Contains("operation_id", StringComparison.OrdinalIgnoreCase) == true;

    private static ApiResponse<TableServiceSessionDto> NotFound() =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            "Table service session was not found.", ErrorCodes.TableServiceSessionNotFound);

    private static ApiResponse<TableServiceSessionDto> Stale(int version) =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(
            $"The service session is stale; current version is {version}. Refresh the bill before paying.",
            ErrorCodes.TableServiceSessionStale);
}
