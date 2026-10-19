using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;

namespace RestaurantSystem.Api.Features.CashierPrinting.Services;

public sealed partial class CashierPrintQueue
{
    private async Task<OrderDto> BuildReceiptAsync(QueueCashierPrintRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ServiceSessionId is Guid sessionId)
        {
            var session = await TableServiceSessionRowLock.LoadAsync(context, sessionId, cancellationToken)
                ?? throw new NotFoundException("Table visit not found.");
            if (session.Version != request.ExpectedVersion)
                throw new ConflictException("The table bill changed. Refresh before printing.");
            var bill = await bills.AssembleAsync(sessionId, cancellationToken)
                ?? throw new NotFoundException("Table bill not found.");
            if (bill.IsAmbiguous || bill.Orders.Count == 0)
                throw new ConflictException("Resolve the table bill before printing.");
            return CashierReceiptSnapshot.FromBill(bill);
        }
        else
        {
            var order = await context.Orders.IncludeOrderLineGraph().AsSplitQuery().Include(order => order.Payments)
                .SingleOrDefaultAsync(order => order.Id == request.OrderId, cancellationToken)
                ?? throw new NotFoundException("Order not found.");
            if (order.Version != request.ExpectedVersion)
                throw new ConflictException("The order changed. Refresh before printing.");
            if (order.ExternalReference is not null)
                throw new ConflictException("Use the marketplace receipt workflow for this order.");
            return CashierReceiptSnapshot.Sanitize(await mapping.MapToOrderDtoAsync(order, cancellationToken));
        }
    }
}
