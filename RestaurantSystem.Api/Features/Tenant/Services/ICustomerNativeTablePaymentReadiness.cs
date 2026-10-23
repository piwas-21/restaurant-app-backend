namespace RestaurantSystem.Api.Features.Tenant.Services;

public interface ICustomerNativeTablePaymentReadiness
{
    bool IsReady(string? currency);
}
