using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public static class CustomerLoyaltyReadAccess
{
    public sealed record Authority(Guid Owner, Guid Tenant, string Currency);

    public static async Task<Authority> RequireEnabledAsync(CustomMediator mediator,
        ICurrentUserService current, CancellationToken cancellationToken)
    {
        var owner = CustomerAccountAccess.RequireOwner(current);
        var bootstrap = (await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(1), cancellationToken)).Data;
        if (bootstrap?.Capabilities.GetValueOrDefault("loyalty") != "enabled"
            || string.IsNullOrWhiteSpace(bootstrap.Currency))
            throw new NotFoundException("Customer loyalty is unavailable.");
        return new(owner, bootstrap.TenantId, bootstrap.Currency);
    }

    public static async Task RequireCurrentAsync(ApplicationDbContext context, CustomMediator mediator,
        ICurrentUserService current, Authority original, CancellationToken cancellationToken)
    {
        if (!await CustomerAccountAccess.ForOwner(context, original.Owner).AsNoTracking().AnyAsync(cancellationToken)
            || original != await RequireEnabledAsync(mediator, current, cancellationToken))
            throw new NotFoundException("Customer loyalty is unavailable.");
    }
}
