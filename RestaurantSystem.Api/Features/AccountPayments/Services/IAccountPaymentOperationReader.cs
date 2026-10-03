using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountPaymentOperationReader
{
    Task<AccountPaymentOperationDto> GetAttemptAsync(
        Guid sessionId, Guid operationId, CancellationToken cancellationToken);

    Task<AccountEqualSharePlanDto> GetEqualSharePlanAsync(
        Guid sessionId, Guid operationId, CancellationToken cancellationToken);
}
