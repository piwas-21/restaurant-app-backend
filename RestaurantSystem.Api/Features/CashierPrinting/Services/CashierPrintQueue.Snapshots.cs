using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.CashierPrinting.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
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
            var current = await tickets.ReadAsync(bill.Orders[0].Id, cancellationToken);
            EnsureResolved(current);
            return CashierReceiptSnapshot.FromBill(bill, current);
        }
        else
        {
            var order = await context.Orders.AsNoTracking().Include(order => order.ExternalReference)
                .SingleOrDefaultAsync(order => order.Id == request.OrderId, cancellationToken)
                ?? throw new NotFoundException("Order not found.");
            if (order.Version != request.ExpectedVersion)
                throw new ConflictException("The order changed. Refresh before printing.");
            if (order.ExternalReference is not null)
                throw new ConflictException("Use the marketplace receipt workflow for this order.");
            var current = await tickets.ReadAsync(order.Id, cancellationToken);
            EnsureResolved(current);
            return CurrentCashierReceipt.FromOrders(order.Id, current);
        }
    }
    private static void EnsureResolved(IReadOnlyList<OrderDto> orders)
    {
        if (orders.Any(order => order.HasPendingBillingCorrection))
            throw new ConflictException("Resolve the billing correction before printing.");
    }
}
