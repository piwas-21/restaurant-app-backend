using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;

namespace RestaurantSystem.Api.Features.Basket.Services.Operations;

internal static class CustomerBasketAvailability
{
    public static async Task EnsureOrderingAsync(CustomMediator mediator, CancellationToken cancellationToken)
    {
        await ReadOrderingAsync(mediator, cancellationToken);
    }

    public static async Task<CustomerMobileBootstrapDto> ReadOrderingAsync(CustomMediator mediator, CancellationToken cancellationToken)
    {
        var bootstrap = await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(1), cancellationToken);
        var data = bootstrap.Data;
        if (data is null || data.Capabilities.GetValueOrDefault("ordering") != "enabled")
            throw new NotFoundException("Customer mobile ordering is unavailable.");
        return data;
    }

    public static async Task EnsureChannelAsync(IOrderTypeConfigurationService configuration,
        OrderType orderType, CancellationToken cancellationToken)
    {
        if (!(await configuration.GetEnabledOrderTypesAsync(cancellationToken)).Contains(orderType))
            throw new BadRequestException("This order type is currently unavailable.", ErrorCodes.OrderTypeNotAvailable);
    }
}
