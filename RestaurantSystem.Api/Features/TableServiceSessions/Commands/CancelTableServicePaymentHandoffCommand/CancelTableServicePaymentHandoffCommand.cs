using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.CancelTableServicePaymentHandoffCommand;

public record CancelTableServicePaymentHandoffCommand : ICommand<ApiResponse<TableServiceSessionDto>>
{
    [JsonIgnore]
    public Guid ServiceSessionId { get; set; }

    [JsonRequired]
    public Guid OperationId { get; init; }

    [JsonRequired]
    public int ExpectedVersion { get; init; }
}

public sealed class CancelTableServicePaymentHandoffCommandHandler
    : ICommandHandler<CancelTableServicePaymentHandoffCommand, ApiResponse<TableServiceSessionDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITableServiceSessionReader _reader;
    private readonly TimeProvider _timeProvider;

    public CancelTableServicePaymentHandoffCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        ITableServiceSessionReader reader,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _currentUser = currentUser;
        _reader = reader;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<TableServiceSessionDto>> Handle(
        CancelTableServicePaymentHandoffCommand command, CancellationToken cancellationToken)
    {
        var replay = await ResolveReplayAsync(command, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        await using var mutation = await TableServicePaymentHandoffCommandSupport.LockOpenForMutationAsync(
            _context,
            command.ServiceSessionId,
            command.ExpectedVersion,
            token => ResolveReplayAsync(command, token),
            "A closed table service session has no cancellable handoff.",
            cancellationToken);
        if (mutation.Response is not null)
        {
            return mutation.Response;
        }
        var session = mutation.Session!;
        try
        {
            var handoff = await _context.TableServicePaymentHandoffs
                .SingleOrDefaultAsync(value => value.ServiceSessionId == session.Id
                    && value.Status == TableServicePaymentHandoffStatus.Requested, cancellationToken);
            if (handoff is null)
            {
                return Failure("There is no pending cashier collection request to cancel.",
                    ErrorCodes.TableServicePaymentHandoffNotRequestable);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            handoff.Status = TableServicePaymentHandoffStatus.Cancelled;
            handoff.CancellationOperationId = command.OperationId;
            handoff.CancellationExpectedVersion = command.ExpectedVersion;
            handoff.CancelledAt = now;
            handoff.CancelledBy = _currentUser.GetAuditIdentifier();
            session.Version++;
            await _context.SaveChangesAsync(cancellationToken);
            await mutation.Transaction.CommitAsync(cancellationToken);
            return await TableServicePaymentHandoffCommandSupport.ReadSuccessAsync(
                _reader, session.Id, "Payment handoff cancelled", cancellationToken);
        }
        catch (DbUpdateException exception) when (IsOperationConflict(exception))
        {
            await TableServicePaymentHandoffCommandSupport.ResetTransactionAsync(
                _context, mutation.Transaction, cancellationToken);
            return await ResolveReplayAsync(command, cancellationToken)
                ?? Failure("This cancellation operation conflicted with another request.",
                    ErrorCodes.TableServicePaymentHandoffCancellationMismatch);
        }
    }

    private async Task<ApiResponse<TableServiceSessionDto>?> ResolveReplayAsync(
        CancelTableServicePaymentHandoffCommand command, CancellationToken cancellationToken)
    {
        var handoff = await _context.TableServicePaymentHandoffs
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.CancellationOperationId == command.OperationId,
                cancellationToken);
        if (handoff is null)
        {
            return null;
        }

        if (handoff.ServiceSessionId != command.ServiceSessionId
            || handoff.CancellationExpectedVersion != command.ExpectedVersion)
        {
            return Failure("This cancellation operation id was already used for a different session or version.",
                ErrorCodes.TableServicePaymentHandoffCancellationMismatch);
        }

        return await TableServicePaymentHandoffCommandSupport.ReadSuccessAsync(
            _reader, handoff.ServiceSessionId, "Payment handoff cancellation already recorded", cancellationToken);
    }

    private static bool IsOperationConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName?.Contains("cancellation_operation_id", StringComparison.OrdinalIgnoreCase) == true;

    private static ApiResponse<TableServiceSessionDto> Failure(string error, string code) =>
        TableServicePaymentHandoffCommandSupport.Failure(error, code);
}
