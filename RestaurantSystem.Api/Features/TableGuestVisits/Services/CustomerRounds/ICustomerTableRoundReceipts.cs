using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

public interface ICustomerTableRoundReceipts
{
    Task LockAsync(Guid operationId, CancellationToken cancellationToken);
    Task RequireUnusedAsync(Guid operationId, CancellationToken cancellationToken);
    Task<CustomerTableRoundResolutionDto?> ReadAsync(CustomerTableRoundOwner owner,
        CustomerTableRoundProofDto proof, CustomerTableRoundBinding binding, CancellationToken cancellationToken);
    Task<CustomerTableRoundResolutionDto> SaveAsync(CustomerTableRoundOwner owner,
        CustomerTableRoundProofDto proof, CustomerTableRoundBinding binding, Guid? orderId, CancellationToken cancellationToken);
}
