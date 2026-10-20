using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.Payments.Services;

public sealed record StripeProviderContext(string ConnectedAccountId, bool LiveMode)
{
    public static StripeProviderContext Read(IStripeGateway gateway, StripeSettings settings)
    {
        if (!gateway.IsConfigured)
            throw new BadRequestException("Online payment is not available for this restaurant.");
        var key = settings.PlatformApiKey;
        var live = key.StartsWith("sk_live_", StringComparison.Ordinal)
            || key.StartsWith("rk_live_", StringComparison.Ordinal);
        var test = key.StartsWith("sk_test_", StringComparison.Ordinal)
            || key.StartsWith("rk_test_", StringComparison.Ordinal);
        if (!live && !test)
            throw new BadRequestException("The online payment environment requires configuration.");
        return new(gateway.ConnectedAccountId, live);
    }
}
