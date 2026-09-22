using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public static class TableServicePaymentHandoffCommandSupport
{
    public static async Task<TableServicePaymentHandoffMutationLock> LockOpenForMutationAsync(
        ApplicationDbContext context,
        Guid sessionId,
        int expectedVersion,
        Func<CancellationToken, Task<ApiResponse<TableServiceSessionDto>?>> resolveReplay,
        string closedError,
        CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken);
        if (session is null)
        {
            return new(transaction, null, Failure(
                "Table service session was not found.", ErrorCodes.TableServiceSessionNotFound));
        }

        var replay = await resolveReplay(cancellationToken);
        if (replay is not null)
        {
            return new(transaction, session, replay);
        }

        if (session.Status != TableServiceSessionStatus.Open)
        {
            return new(transaction, session, Failure(
                closedError, ErrorCodes.TableServicePaymentHandoffNotRequestable));
        }

        return session.Version == expectedVersion
            ? new(transaction, session, null)
            : new(transaction, session, Failure(
                $"The service session is stale; current version is {session.Version}.",
                ErrorCodes.TableServiceSessionStale));
    }

    public static async Task<ApiResponse<TableServiceSessionDto>> ReadSuccessAsync(
        ITableServiceSessionReader reader,
        Guid sessionId,
        string message,
        CancellationToken cancellationToken)
    {
        var session = await reader.ReadAsync(sessionId, cancellationToken);
        return session is null
            ? Failure("Table service session was not found.", ErrorCodes.TableServiceSessionNotFound)
            : ApiResponse<TableServiceSessionDto>.SuccessWithData(session, message);
    }

    public static async Task ResetTransactionAsync(
        ApplicationDbContext context,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await context.Database.UseTransactionAsync(null, cancellationToken);
            context.ChangeTracker.Clear();
        }
    }

    public static ApiResponse<TableServiceSessionDto> Failure(string error, string code) =>
        ApiResponse<TableServiceSessionDto>.FailureWithCode(error, code);
}

public sealed record TableServicePaymentHandoffMutationLock(
    IDbContextTransaction Transaction,
    TableServiceSession? Session,
    ApiResponse<TableServiceSessionDto>? Response) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Transaction.DisposeAsync();
}
