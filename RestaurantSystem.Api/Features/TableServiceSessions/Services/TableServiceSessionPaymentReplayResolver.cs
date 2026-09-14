using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.AddTableServiceSessionPaymentCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

public sealed class TableServiceSessionPaymentReplayResolver : ITableServiceSessionPaymentReplayResolver
{
    private readonly ApplicationDbContext _context;
    private readonly ITableServiceSessionReader _reader;

    public TableServiceSessionPaymentReplayResolver(
        ApplicationDbContext context, ITableServiceSessionReader reader)
    {
        _context = context;
        _reader = reader;
    }

    public async Task<ApiResponse<TableServiceSessionDto>?> ResolveAsync(
        AddTableServiceSessionPaymentCommand command, CancellationToken cancellationToken)
    {
        var operation = await _context.TableBillPaymentOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.OperationId == command.OperationId, cancellationToken);
        if (operation is null)
        {
            return null;
        }

        if (operation.ServiceSessionId != command.ServiceSessionId
            || operation.ExpectedVersion != command.ExpectedVersion
            || operation.PaymentMethod != command.PaymentMethod
            || operation.Amount != command.Amount
            || !string.Equals(operation.Currency, command.Currency, StringComparison.OrdinalIgnoreCase)
            || operation.TransactionId != command.TransactionId
            || operation.ReferenceNumber != command.ReferenceNumber
            || operation.CardLastFourDigits != command.CardLastFourDigits
            || operation.CardType != command.CardType
            || operation.PaymentNotes != command.PaymentNotes)
        {
            return ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "This operation id was already used for a different session or payment payload.",
                ErrorCodes.TableServiceSessionPaymentOperationMismatch);
        }

        var session = await _reader.ReadAsync(command.ServiceSessionId, cancellationToken);
        return session is null
            ? ApiResponse<TableServiceSessionDto>.FailureWithCode(
                "The payment was recorded, but the service session could not be read.",
                ErrorCodes.TableServiceSessionNotFound)
            : ApiResponse<TableServiceSessionDto>.SuccessWithData(session, "Payment already recorded");
    }
}
