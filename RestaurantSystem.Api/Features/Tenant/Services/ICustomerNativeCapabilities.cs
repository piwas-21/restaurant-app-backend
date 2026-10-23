namespace RestaurantSystem.Api.Features.Tenant.Services;

public interface ICustomerNativeCapabilities
{
    CustomerNativeCapabilityResolution Resolve(IReadOnlyDictionary<string, string> capabilities, string? currency);
}
