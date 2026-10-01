using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class ExternalOrderLocalMutationGuard
{
    internal static void RequireLocalOrder(Order order)
    {
        if (order.ExternalReference is not null)
            throw new ForbiddenException("This order is managed by its delivery channel. Use the channel decision workflow.");
    }
}
