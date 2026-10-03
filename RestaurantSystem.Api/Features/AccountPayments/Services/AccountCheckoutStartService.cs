using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed class AccountCheckoutStartService(IAccountCheckoutJournalStore journals,
    IAccountCheckoutReconciler reconciler) : IAccountCheckoutStartService
{
    public async Task<AccountCheckoutStartDto> StartGuestAsync(Guid sessionId, Guid operationId,
        int expectedVersion, string? participantCredential, string? receiptCredential,
        CancellationToken cancellationToken)
    {
        var journal = await journals.FreezeGuestAsync(sessionId, operationId, expectedVersion,
            participantCredential, receiptCredential, cancellationToken);
        return await reconciler.ReconcileAsync(journal.AttemptId, cancellationToken);
    }
}
