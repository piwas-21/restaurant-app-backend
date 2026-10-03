using RestaurantSystem.Api.Features.AccountPayments.Dtos;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountPaymentOperationReader
{
    Task<AccountPaymentOperationDto> GetAttemptAsync(
        Guid sessionId, Guid operationId, CancellationToken cancellationToken);

    Task<AccountPaymentOperationDto> GetGuestAttemptAsync(
        Guid sessionId, Guid operationId, string? participantCredential, CancellationToken cancellationToken);

    Task<AccountEqualSharePlanDto> GetEqualSharePlanAsync(
        Guid sessionId, Guid operationId, CancellationToken cancellationToken);

    Task<AccountEqualSharePlanDto> GetGuestEqualSharePlanAsync(
        Guid sessionId, Guid operationId, string? participantCredential, CancellationToken cancellationToken);
}
