using RestaurantSystem.Api.Features.Tenant.Dtos;

namespace RestaurantSystem.Api.Common.Partner;

public interface ITenantBranding
{
    Task<TenantPartnerDto> GetAsync(CancellationToken cancellationToken);
}
