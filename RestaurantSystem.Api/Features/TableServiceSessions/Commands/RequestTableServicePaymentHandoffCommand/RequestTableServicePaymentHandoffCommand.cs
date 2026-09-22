using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
namespace RestaurantSystem.Api.Features.TableServiceSessions.Commands.RequestTableServicePaymentHandoffCommand;

public record RequestTableServicePaymentHandoffCommand : ICommand<ApiResponse<TableServiceSessionDto>>
{
    [JsonIgnore]
    public Guid ServiceSessionId { get; set; }

    [JsonRequired]
    public Guid OperationId { get; init; }

    [JsonRequired]
    public int ExpectedVersion { get; init; }
}

public sealed class RequestTableServicePaymentHandoffCommandHandler
    : ICommandHandler<RequestTableServicePaymentHandoffCommand, ApiResponse<TableServiceSessionDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ITableServiceSessionReader _reader;
    private readonly decimal _paymentTolerance;
    private readonly TimeProvider _timeProvider;

    public RequestTableServicePaymentHandoffCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUser,
        ITableServiceSessionReader reader,
        IOptions<TableServiceSessionSettings>? settings = null,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _currentUser = currentUser;
        _reader = reader;
        _paymentTolerance = (settings?.Value ?? new TableServiceSessionSettings()).PaymentTolerance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<TableServiceSessionDto>> Handle(
        RequestTableServicePaymentHandoffCommand command, CancellationToken cancellationToken)
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
            "A closed table service session cannot request collection.",
            cancellationToken);
        if (mutation.Response is not null)
        {
            return mutation.Response;
        }
        var session = mutation.Session!;
        try
        {
            if (await TableServicePaymentHandoffRules.HasPendingAsync(
                _context, session.Id, cancellationToken))
            {
                return Failure("A cashier collection request is already pending for this session.",
                    ErrorCodes.TableServicePaymentHandoffAlreadyPending);
            }

            if (await TableServicePaymentHandoffRules.HasBlockingLegacyAsync(
                _context, session, _paymentTolerance, cancellationToken))
            {
                return Failure(TableBillTargetResolver.AmbiguousMessage,
                    ErrorCodes.TableServiceSessionAmbiguous);
            }

            var amount = await TableServicePaymentHandoffRules.ReadOutstandingAsync(
                _context, session.Id, cancellationToken);
            if (amount <= _paymentTolerance)
            {
                return Failure("The service session has no outstanding balance to collect.",
                    ErrorCodes.TableServicePaymentHandoffNotRequestable);
            }

            var currency = CurrencyCode.Normalize(session.Currency)
                ?? CurrencyCode.Normalize(await _context.RestaurantInfo
                    .AsNoTracking()
                    .Select(info => info.Currency)
                    .FirstOrDefaultAsync(cancellationToken));
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            _context.TableServicePaymentHandoffs.Add(new TableServicePaymentHandoff
            {
                Id = Guid.NewGuid(),
                ServiceSessionId = session.Id,
                OperationId = command.OperationId,
                ExpectedVersion = command.ExpectedVersion,
                RequestedAmount = amount,
                RequestedCurrency = currency,
                Status = TableServicePaymentHandoffStatus.Requested,
                RequestedAt = now,
                CreatedAt = now,
                CreatedBy = _currentUser.GetAuditIdentifier()
            });
            session.Version++;
            await _context.SaveChangesAsync(cancellationToken);
            await mutation.Transaction.CommitAsync(cancellationToken);

            return await TableServicePaymentHandoffCommandSupport.ReadSuccessAsync(
                _reader, session.Id, "Payment handoff requested", cancellationToken);
        }
        catch (DbUpdateException exception) when (TableServicePaymentHandoffConflicts.IsOperation(exception))
        {
            await TableServicePaymentHandoffCommandSupport.ResetTransactionAsync(
                _context, mutation.Transaction, cancellationToken);
            return await ResolveReplayAsync(command, cancellationToken)
                ?? Failure("This handoff operation conflicted with another request.",
                    ErrorCodes.TableServicePaymentHandoffAlreadyPending);
        }
        catch (DbUpdateException exception) when (TableServicePaymentHandoffConflicts.IsPending(exception))
        {
            await TableServicePaymentHandoffCommandSupport.ResetTransactionAsync(
                _context, mutation.Transaction, cancellationToken);
            return Failure("A cashier collection request is already pending for this session.",
                ErrorCodes.TableServicePaymentHandoffAlreadyPending);
        }
    }

    private async Task<ApiResponse<TableServiceSessionDto>?> ResolveReplayAsync(
        RequestTableServicePaymentHandoffCommand command, CancellationToken cancellationToken)
    {
        var operation = await _context.TableServicePaymentHandoffs
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.OperationId == command.OperationId, cancellationToken);
        if (operation is null)
        {
            return null;
        }

        if (operation.ServiceSessionId != command.ServiceSessionId
            || operation.ExpectedVersion != command.ExpectedVersion)
        {
            return Failure("This handoff operation id was already used for a different session or version.",
                ErrorCodes.TableServicePaymentHandoffOperationMismatch);
        }

        return await TableServicePaymentHandoffCommandSupport.ReadSuccessAsync(
            _reader, operation.ServiceSessionId, "Payment handoff already recorded", cancellationToken);
    }

    private static ApiResponse<TableServiceSessionDto> Failure(string error, string code) =>
        TableServicePaymentHandoffCommandSupport.Failure(error, code);
}
