using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

public sealed record AccountCheckoutCanonicalEvidence(
    AccountStripeSession Session, AccountStripeIntent? Intent, AccountStripeCharge? Charge)
{
    public static AccountStripeExpectation Expect(AccountCheckoutJournal journal) => new()
    {
        AttemptId = journal.AttemptId,
        AmountMinor = journal.AmountMinor,
        Currency = journal.Currency,
        Context = new AccountStripeContext(journal.ProviderAccountId, journal.ProviderLiveMode),
        SessionId = journal.ProviderSessionId,
        IntentId = journal.ProviderIntentId
    };

    public void Validate(AccountCheckoutJournal journal)
    {
        var expected = Expect(journal);
        AccountStripeEvidence.RequireSession(expected, Session);
        if (Intent is not null) AccountStripeEvidence.RequireIntent(expected, Session, Intent);
        if (Charge is not null && Intent is not null)
            AccountStripeChargeEvidence.RequireCharge(expected, Session, Intent, Charge);
    }

    public bool HasUnreversedCapture(AccountCheckoutJournal journal) => Intent is not null && Charge is not null
        && AccountStripeChargeEvidence.HasUnreversedCapture(Expect(journal), Session, Intent, Charge);

    public bool CanRelease(AccountCheckoutJournal journal) =>
        AccountStripeEvidence.CanRelease(Expect(journal), Session, Intent);
}
