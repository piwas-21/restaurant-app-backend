namespace RestaurantSystem.Api.Features.Tenant.Services;

public interface ICustomerNativeAuthenticationCapabilities
{
    CustomerNativeAuthenticationFlags Resolve(bool accountsEnabled);
}
