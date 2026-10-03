using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public interface IAccountCheckoutJournalStore
{
    Task<AccountCheckoutJournal> FreezeGuestAsync(Guid sessionId, Guid operationId, int expectedVersion,
        string? participantCredential, string? receiptCredential, CancellationToken cancellationToken);
    Task<AccountCheckoutJournal> ReadAsync(Guid attemptId, CancellationToken cancellationToken);
}
