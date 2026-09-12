using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.AddTableBillPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

public class TableBillPaymentOperationReplayResolver : ITableBillPaymentOperationReplayResolver
{
    private readonly ApplicationDbContext _context;
    private readonly ITableBillAssembler _billAssembler;

    public TableBillPaymentOperationReplayResolver(ApplicationDbContext context, ITableBillAssembler billAssembler)
    {
        _context = context;
        _billAssembler = billAssembler;
    }

    public async Task<ApiResponse<TableBillDto>?> ResolveAsync(
        AddTableBillPaymentCommand command, CancellationToken cancellationToken)
    {
        var operation = await _context.TableBillPaymentOperations.AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.OperationId == command.OperationId, cancellationToken);
        if (operation is null)
        {
            return null;
        }

        if (!Matches(operation, command))
        {
            return ApiResponse<TableBillDto>.Failure(
                "This operation id was already used for a different table or payment payload");
        }

        var bill = await _billAssembler.AssembleAsync(operation.TableNumber, cancellationToken);
        return bill is null
            ? ApiResponse<TableBillDto>.Failure(
                "The payment was already recorded, but the bill could not be refreshed. Please reopen the bill")
            : ApiResponse<TableBillDto>.SuccessWithData(bill, "Payment already recorded");
    }

    private static bool Matches(TableBillPaymentOperation operation, AddTableBillPaymentCommand command) =>
        operation.TableNumber == command.TableNumber
        && operation.ServiceSessionId == command.ServiceSessionId
        && string.Equals(operation.Currency, command.Currency, StringComparison.OrdinalIgnoreCase)
        && operation.PaymentMethod == command.PaymentMethod
        && operation.Amount == command.Amount
        && operation.TransactionId == command.TransactionId
        && operation.ReferenceNumber == command.ReferenceNumber
        && operation.CardLastFourDigits == command.CardLastFourDigits
        && operation.CardType == command.CardType
        && operation.PaymentNotes == command.PaymentNotes;
}
