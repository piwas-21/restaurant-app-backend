using RestaurantSystem.Api.Features.Tenant.Dtos;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public interface ICustomerTableRoundAccess
{
    CustomerTableRoundOwner Capture(string sessionId, int schemaVersion, Guid tenantId);
    void RequireOriginal(string sessionId, int schemaVersion, Guid tenantId, CustomerTableRoundOwner owner);
    Task LockCustomerAsync(CustomerTableRoundOwner owner, CancellationToken cancellationToken);
    Task<CustomerMobileBootstrapDto> RequireNewAsync(CustomerTableRoundOwner owner, CancellationToken cancellationToken);
    Task RevalidateAsync(CustomerTableRoundOwner owner, CustomerTableRoundBinding binding, bool acceptedRound, CancellationToken cancellationToken);
}
