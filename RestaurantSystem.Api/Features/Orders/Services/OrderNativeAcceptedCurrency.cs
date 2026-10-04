using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class OrderNativeAcceptedCurrency
{
    internal static Task<string?> ReadTenantCurrencyAsync(
        ApplicationDbContext context, CancellationToken cancellationToken) =>
        context.RestaurantInfo.AsNoTracking()
            .Select(restaurant => restaurant.Currency)
            .FirstOrDefaultAsync(cancellationToken);
}
