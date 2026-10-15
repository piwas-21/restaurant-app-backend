using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Orders.Interfaces;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Rechecks guest availability at acceptance, including direct and stale basket orders.</summary>
public sealed class OrderAvailabilityGuard(
    ICurrentUserService currentUser,
    IOrderTypeConfigurationService orderTypes) : IOrderAvailabilityGuard
{
    public async Task EnsureAvailableAsync(OrderType orderType, CancellationToken cancellationToken = default)
    {
        if (currentUser.IsStaff)
        {
            return;
        }

        var available = await orderTypes.GetEnabledOrderTypesAsync(cancellationToken);
        if (!available.Contains(orderType))
        {
            throw new BadRequestException(
                "This order type is currently unavailable. The restaurant may be closed. Your basket is still saved.",
                ErrorCodes.OrderTypeNotAvailable);
        }
    }
}
