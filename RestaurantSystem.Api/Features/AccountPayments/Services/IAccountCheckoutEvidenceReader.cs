using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountCheckoutEvidenceReader
{
    Task<AccountCheckoutCanonicalEvidence> ReadAsync(AccountCheckoutJournal journal,
        bool allowOriginalCreateRetry, CancellationToken cancellationToken);
}
