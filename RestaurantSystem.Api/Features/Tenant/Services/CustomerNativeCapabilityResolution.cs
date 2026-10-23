namespace RestaurantSystem.Api.Features.Tenant.Services;

public sealed record CustomerNativeCapabilityResolution(IReadOnlyDictionary<string, string> Capabilities,
    CustomerNativeAuthenticationFlags Authentication);
