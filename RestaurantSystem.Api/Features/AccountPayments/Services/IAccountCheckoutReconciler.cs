using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountCheckoutReconciler
{
    Task<AccountCheckoutStartDto> ReconcileAsync(Guid attemptId, CancellationToken cancellationToken);
}
