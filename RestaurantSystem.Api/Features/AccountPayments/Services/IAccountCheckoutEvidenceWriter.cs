using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountCheckoutEvidenceWriter
{
    Task<AccountCheckoutJournal> RecordAsync(AccountCheckoutJournal original,
        AccountCheckoutCanonicalEvidence evidence, CancellationToken cancellationToken);
}
