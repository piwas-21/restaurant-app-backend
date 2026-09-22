using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.AddTableBillPaymentCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.Orders.Services;

public static class TableBillPaymentResponseReader
{
    public static async Task<ApiResponse<TableBillDto>> ReadAsync(
        ITableBillAssembler bills,
        ILogger logger,
        AddTableBillPaymentCommand command,
        decimal appliedTotal,
        int orderCount,
        CancellationToken cancellationToken)
    {
        TableBillDto? bill;
        try
        {
            bill = command.ServiceSessionId.HasValue
                ? await bills.AssembleAsync(command.ServiceSessionId.Value, cancellationToken)
                : await bills.AssembleAsync(command.TableNumber, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Bill for table {TableNumber} could not be re-read after a committed payment",
                command.TableNumber);
            return Failure();
        }

        return bill is null
            ? Failure()
            : ApiResponse<TableBillDto>.SuccessWithData(
                bill, $"Payment of {appliedTotal:0.00} applied across {orderCount} order(s)");
    }

    private static ApiResponse<TableBillDto> Failure() => ApiResponse<TableBillDto>.Failure(
        "The payment was recorded, but the bill could not be refreshed. Please reopen the bill");
}
