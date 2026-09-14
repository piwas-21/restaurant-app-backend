using RestaurantSystem.Api.Features.Orders.Commands.AddTableBillPaymentCommand;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

public static class TableBillPaymentAllocation
{
    public static async Task<AllocationResult> ApplyAsync(
        IReadOnlyList<BillRound> orders,
        OrderPaymentTender tender,
        int tableNumber,
        AllocationResult allocation,
        IOrderPaymentApplicator paymentApplicator,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var order in orders)
        {
            if (allocation.Left <= 0)
            {
                break;
            }

            var outstanding = Math.Max(0, order.RemainingAmount);
            if (outstanding <= 0)
            {
                continue;
            }

            var share = Math.Min(outstanding, allocation.Left);
            var result = await paymentApplicator.ApplyToOrderAsync(
                order.Id, tender with { Amount = share }, cancellationToken);
            if (result.Outcome != OrderPaymentApplicationOutcome.Applied || result.Order is null)
            {
                logger.LogWarning(
                    "Bill payment for table {TableNumber} rolled back on order {OrderId}: {Outcome}",
                    tableNumber, order.Id, result.Outcome);
                return allocation with { Success = false };
            }

            allocation.Orders.Add(result.Order.OrderNumber);
            allocation = allocation with { Left = allocation.Left - share };
        }

        return allocation;
    }
}

public sealed record BillRound(Guid Id, decimal RemainingAmount);
public sealed record AllocationResult(decimal Left, List<string> Orders, bool Success = true);
