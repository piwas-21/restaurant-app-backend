using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Domain.Entities;
using Stripe;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Canonical session, intent and charge reads execute with no database transaction.</summary>
public sealed class AccountCheckoutEvidenceReader(IAccountStripeCheckoutClient provider, TimeProvider clock)
    : IAccountCheckoutEvidenceReader
{
    public async Task<AccountCheckoutCanonicalEvidence> ReadAsync(AccountCheckoutJournal journal,
        bool allowOriginalCreateRetry, CancellationToken cancellationToken)
    {
        if (provider.ReadContext() != new AccountStripeContext(journal.ProviderAccountId, journal.ProviderLiveMode))
            throw new ConflictException("Reconcile the original provider account and environment.");
        AccountStripeSession session;
        if (journal.ProviderSessionId is not null)
            session = await provider.GetAsync(journal.ProviderSessionId, cancellationToken)
                ?? throw new ConflictException("The original provider checkout is unavailable. Its reservation remains held.");
        else if (allowOriginalCreateRetry)
            session = await provider.CreateAsync(
                AccountCheckoutReplayPayload.Read(journal, clock.GetUtcNow().UtcDateTime), cancellationToken);
        else
            throw new ConflictException("The original checkout requires bounded provider recovery.");
        AccountStripeEvidence.RequireSession(AccountCheckoutCanonicalEvidence.Expect(journal), session);
        if (journal.CancelRequestedAt.HasValue && session.Status == "open" && session.PaymentStatus == "unpaid")
            session = await ExpireAndReadAsync(session.Id, cancellationToken);
        var intent = session.IntentId is null ? null
            : await provider.GetIntentAsync(session.IntentId, cancellationToken);
        if (session.IntentId is not null && intent is null)
            throw new ConflictException("The original provider intent is unavailable. Its reservation remains held.");
        if (intent is not null)
            AccountStripeEvidence.RequireIntent(AccountCheckoutCanonicalEvidence.Expect(journal), session, intent);
        var charge = intent?.ChargeId is not string chargeId ? null
            : await provider.GetChargeAsync(chargeId, cancellationToken);
        if (intent?.ChargeId is not null && charge is null)
            throw new ConflictException("The original provider charge is unavailable. Its reservation remains held.");
        var evidence = new AccountCheckoutCanonicalEvidence(session, intent, charge);
        evidence.Validate(journal);
        return evidence;
    }
    private async Task<AccountStripeSession> ExpireAndReadAsync(string sessionId, CancellationToken cancellationToken)
    {
        try { await provider.ExpireAsync(sessionId, cancellationToken); }
        catch (StripeException)
        {
            // A payment can win the expiry race. The canonical read, rather than this error, decides the outcome.
        }
        return await provider.GetAsync(sessionId, cancellationToken)
            ?? throw new ConflictException("Canonical cancellation is unavailable. Its reservation remains held.");
    }
}
