using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.MarkTableReadyCommand;

public sealed partial class MarkTableReadyCommandHandler
{
    private async Task<ApiResponse<TableReadinessOperationDto>?> ReplayIfPresentAsync(
        Table table,
        MarkTableReadyCommand command,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var replay = await _context.TableReadyOperations.AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.TableId == table.Id
                && operation.OperationId == command.OperationId, cancellationToken);
        if (replay is null) return null;
        if (replay.ActorUserId != _currentUser.UserId!.Value
            || replay.ActorRole != _currentUser.Role
            || replay.ExpectedReadinessVersion != command.ExpectedReadinessVersion)
        {
            return Failure(
                "This operation id was already used for a different staff request.",
                ErrorCodes.TableReadinessOperationMismatch);
        }

        await transaction.CommitAsync(cancellationToken);
        return TableReadinessOperationResponses.ToResponse(replay);
    }

    private async Task<ApiResponse<TableReadinessOperationDto>?> RecordPreconditionFailureAsync(
        Table table,
        MarkTableReadyCommand command,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!table.IsActive)
        {
            return await RecordFailureAsync(
                table, command, ErrorCodes.TableServiceTableInactive, transaction, cancellationToken);
        }

        if (table.ReadinessVersion != command.ExpectedReadinessVersion)
        {
            return await RecordFailureAsync(
                table, command, ErrorCodes.TableReadinessVersionStale, transaction, cancellationToken);
        }

        var tableNumber = TableReadinessLegacyRules.CanonicalNumber(table.TableNumber);
        if (await TableReadinessLegacyRules.HasStableOpenVisitAsync(
            _context, table.Id, cancellationToken))
        {
            return await RecordFailureAsync(
                table, command, ErrorCodes.TableReadinessVisitOpen, transaction, cancellationToken);
        }

        if (await TableReadinessLegacyRules.HasAmbiguousLegacyOpenVisitAsync(
            _context, tableNumber, cancellationToken))
        {
            return await RecordFailureAsync(
                table, command, ErrorCodes.TableServiceSessionAmbiguous, transaction, cancellationToken);
        }

        if (await TableReadinessLegacyRules.HasBlockingLegacyRoundAsync(
            _context, table.Id, tableNumber, _paymentTolerance, cancellationToken))
        {
            return await RecordFailureAsync(
                table, command, ErrorCodes.TableServiceSessionAmbiguous, transaction, cancellationToken);
        }

        return table.ReadinessState != TableReadinessState.NeedsReset
            ? await RecordFailureAsync(
                table, command, ErrorCodes.TableReadinessNotAvailable, transaction, cancellationToken)
            : null;
    }

    private async Task<ApiResponse<TableReadinessOperationDto>> RecordSuccessAsync(
        Table table,
        MarkTableReadyCommand command,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var operation = CreateOperation(table, command, now);
        operation.Succeeded = true;
        operation.OutcomeState = TableReadinessState.ReadyForGuests;
        operation.OutcomeReadinessVersion = checked(table.ReadinessVersion + 1);
        table.ReadinessState = operation.OutcomeState;
        table.ReadinessVersion = operation.OutcomeReadinessVersion;
        _context.TableReadyOperations.Add(operation);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TableReadinessOperationResponses.ToResponse(operation);
    }

    private async Task<ApiResponse<TableReadinessOperationDto>> RecordFailureAsync(
        Table table,
        MarkTableReadyCommand command,
        string errorCode,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var operation = CreateOperation(table, command, _timeProvider.GetUtcNow().UtcDateTime);
        operation.Succeeded = false;
        operation.OutcomeErrorCode = errorCode;
        operation.OutcomeState = table.ReadinessState;
        operation.OutcomeReadinessVersion = table.ReadinessVersion;
        _context.TableReadyOperations.Add(operation);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TableReadinessOperationResponses.ToResponse(operation);
    }

    private TableReadyOperation CreateOperation(
        Table table, MarkTableReadyCommand command, DateTime recordedAt) => new()
        {
            TableId = table.Id,
            OperationId = command.OperationId,
            ActorUserId = _currentUser.UserId.GetValueOrDefault(),
            ActorRole = _currentUser.Role.GetValueOrDefault(),
            ExpectedReadinessVersion = command.ExpectedReadinessVersion,
            OutcomeState = table.ReadinessState,
            OutcomeReadinessVersion = table.ReadinessVersion,
            RecordedAt = recordedAt,
            CreatedAt = recordedAt,
            CreatedBy = _currentUser.GetAuditIdentifier()
        };

    private static ApiResponse<TableReadinessOperationDto> Failure(string message, string code) =>
        ApiResponse<TableReadinessOperationDto>.FailureWithCode(message, code);
}
