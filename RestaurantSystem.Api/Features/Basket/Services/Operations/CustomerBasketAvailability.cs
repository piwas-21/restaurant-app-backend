using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

internal static class CustomerBasketAvailability
{
    public static async Task EnsureOrderingAsync(CustomMediator mediator, CancellationToken cancellationToken)
    {
        var bootstrap = await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(1), cancellationToken);
        if (bootstrap.Data?.Capabilities.GetValueOrDefault("ordering") != "enabled")
            throw new NotFoundException("Customer mobile ordering is unavailable.");
    }

    public static async Task EnsureChannelAsync(IOrderTypeConfigurationService configuration,
        OrderType orderType, CancellationToken cancellationToken)
    {
        if (!(await configuration.GetEnabledOrderTypesAsync(cancellationToken)).Contains(orderType))
            throw new BadRequestException("This order type is currently unavailable.", ErrorCodes.OrderTypeNotAvailable);
    }
}
