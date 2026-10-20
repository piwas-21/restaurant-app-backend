using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Auth.NativeSocial;

public sealed class NativeSocialAccess(CustomMediator mediator,
    IOptions<CustomerNativeSocialSettings> settings) : INativeSocialAccess
{
    public async Task<Guid> RequireEnabledAsync(string provider, CancellationToken cancellationToken)
    {
        if (provider != "google" || !settings.Value.GoogleEnabled || !settings.Value.IsValid())
            throw Unavailable();
        var bootstrap = (await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(1), cancellationToken)).Data;
        if (bootstrap?.Capabilities.GetValueOrDefault("accounts") != "enabled") throw Unavailable();
        return bootstrap.TenantId;
    }
    private static NotFoundException Unavailable() => new("Native social sign-in is unavailable.");
}
