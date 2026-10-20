using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Payments.Services;

public static class NativeCheckoutExclusion
{
    public static async Task RequireWebAsync(ApplicationDbContext context, Guid orderId,
        CancellationToken cancellationToken)
    {
        if (await context.Set<CustomerOrderOperation>().AsNoTracking().AnyAsync(value => value.OrderId == orderId
                && !value.IsCancelled, cancellationToken))
            throw new ConflictException("Recover this payment in the app with its original order operation.");
    }
}
