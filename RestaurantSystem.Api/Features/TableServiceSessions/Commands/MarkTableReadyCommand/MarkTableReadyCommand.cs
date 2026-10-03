using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.MarkTableReadyCommand;

public sealed record MarkTableReadyCommand : ICommand<ApiResponse<TableReadinessOperationDto>>
{
    [JsonIgnore]
    public Guid TableId { get; set; }

    [JsonRequired]
    public Guid OperationId { get; set; }

    [JsonRequired]
    public int ExpectedReadinessVersion { get; set; }
}

public sealed record TableReadinessOperationDto(
    Guid TableId,
    Guid OperationId,
    string ReadinessState,
    int ReadinessVersion);

public sealed class MarkTableReadyCommandHandler
    : ICommandHandler<MarkTableReadyCommand, ApiResponse<TableReadinessOperationDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITenantFeatures _features;
    private readonly TimeProvider _timeProvider;
    private readonly decimal _paymentTolerance;

    public MarkTableReadyCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        ITenantFeatures features,
        TimeProvider? timeProvider = null,
        IOptions<TableServiceSessionSettings>? settings = null)
    {
        _context = context;
        _currentUser = currentUser;
        _features = features;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
    }

    public async Task<ApiResponse<TableReadinessOperationDto>> Handle(
        MarkTableReadyCommand command, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue
            || _currentUser.Role is not (UserRole.Admin or UserRole.Cashier or UserRole.Server))
        {
            return Failure("An authenticated table-service staff member is required.",
                ErrorCodes.TableReadinessStaffRequired);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var table = await TableServiceSessionRowLock.LoadTableAsync(
            _context, command.TableId, cancellationToken);
        if (table is null)
        {
            return Failure("The selected table was not found.", ErrorCodes.TableServiceTableNotFound);
        }

        var replay = await _context.TableReadyOperations.AsNoTracking()
            .SingleOrDefaultAsync(operation => operation.TableId == table.Id
                && operation.OperationId == command.OperationId, cancellationToken);
        if (replay is not null)
        {
            if (replay.ActorUserId != _currentUser.UserId.Value
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

        if (!_features.TableVisitReadinessV1)
        {
            return Failure(
                "Explicit table readiness is not enabled for this tenant.",
                ErrorCodes.TableReadinessFeatureDisabled);
        }

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

        if (table.ReadinessState != TableReadinessState.NeedsReset)
        {
            return await RecordFailureAsync(
                table, command, ErrorCodes.TableReadinessNotAvailable, transaction, cancellationToken);
        }

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
