using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

/// <summary>Emits the operational event only after the canonical decision commits.</summary>
public sealed class CustomerOrderCancellationNotifier(ApplicationDbContext context, IOrderMappingService mapping,
    IOrderEventService events) : ICustomerOrderCancellationNotifier
{
    public async Task NotifyAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await context.Orders.AsNoTracking().Include(value => value.Items)
            .Include(value => value.Payments).Include(value => value.StatusHistory).AsSplitQuery()
            .SingleOrDefaultAsync(value => value.Id == orderId && !value.IsDeleted, cancellationToken);
        if (order is null) return;
        var dto = await mapping.MapToOrderDtoAsync(order, cancellationToken);
        await events.NotifyOrderStatusChanged(dto, OrderStatus.PendingApproval.ToString());
    }
}
