using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountPaymentCaptureWriter
{
    /// <summary>Records an already reviewed manual contribution inside the caller's locked transaction.</summary>
    Task RecordVerifiedProviderAsync(AccountPaymentAttempt attempt, AccountCheckoutJournal journal,
        CancellationToken cancellationToken);

    Task RecordManualAsync(AccountPaymentAttempt attempt, CancellationToken cancellationToken);
}
