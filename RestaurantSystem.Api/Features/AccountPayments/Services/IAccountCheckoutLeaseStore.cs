using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountCheckoutLeaseStore
{
    Task<AccountCheckoutJournal?> AcquireAsync(Guid attemptId, CancellationToken cancellationToken);
    Task FinishAsync(Guid attemptId, Guid leaseId, string? failureCode, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ReadDueAsync(CancellationToken cancellationToken);
    Task<bool> ScheduleWebhookWakeupAsync(AccountCheckoutWebhookReferences references,
        AccountStripeContext expectedContext, CancellationToken cancellationToken);
}
