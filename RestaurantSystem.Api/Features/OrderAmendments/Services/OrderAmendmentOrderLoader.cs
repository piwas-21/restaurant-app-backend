using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Queries;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentOrderLoader
{
    internal static Task<Order?> LoadSourceAsync(
        ApplicationDbContext context, Guid orderId, CancellationToken cancellationToken) =>
        context.Orders.IgnoreQueryFilters()
            .Where(order => order.Id == orderId && !order.IsDeleted)
            .IncludeOrderLineGraph()
            .Include(order => order.Payments)
            .Include(order => order.ExternalReference)
            .Include(order => order.ServiceSession)
            .Include(order => order.RoutingStates)
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
}
