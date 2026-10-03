using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountStripeEvidenceTests
{
    private static readonly Guid AttemptId = Guid.Parse("21429c0d-0e1c-407c-b24c-8666c6659275");
    private static readonly AccountStripeContext Context = new("acct_evidence", false);

    private static AccountStripeExpectation Expected() => new()
    {
        AttemptId = AttemptId,
        Context = Context,
        AmountMinor = 1000,
        Currency = "chf",
        SessionId = "cs_evidence",
        IntentId = "pi_evidence"
    };

    private static Dictionary<string, string> Metadata() => new()
    {
        ["account_payment_attempt"] = AttemptId.ToString("D"),
        ["sofra_payment_schema"] = "account-payment-v1"
    };

    private static AccountStripeSession Session() => new()
    {
        Id = "cs_evidence",
        Context = Context,
        Status = "complete",
        PaymentStatus = "paid",
        AmountMinor = 1000,
        Currency = "chf",
        IntentId = "pi_evidence",
        ClientReferenceId = AttemptId.ToString("D"),
        Metadata = Metadata()
    };

    private static AccountStripeIntent Intent() => new()
    {
        Id = "pi_evidence",
        Context = Context,
        Status = "succeeded",
        AmountMinor = 1000,
        ReceivedMinor = 1000,
        Currency = "chf",
        ChargeId = "ch_evidence",
        Metadata = Metadata()
    };

    [Fact]
    public void Uppercase_frozen_currency_matches_canonical_provider_currency()
    {
        var expected = Expected() with { Currency = "CHF" };
        AccountStripeEvidence.HasCaptured(expected, Session(), Intent()).Should().BeTrue();
        AccountStripeEvidence.CanRelease(expected,
            Session() with { Status = "expired", PaymentStatus = "unpaid" },
            Intent() with { Status = "canceled", ReceivedMinor = 0 }).Should().BeTrue();
    }

    [Fact]
    public void Complete_checkout_does_not_book_processing_money()
    {
        var intent = Intent() with { Status = "processing", ReceivedMinor = 0 };
        AccountStripeEvidence.HasCaptured(Expected(), Session(), intent).Should().BeFalse();
    }

    [Fact]
    public void Only_full_canonical_success_with_charge_reference_books_capture()
    {
        AccountStripeEvidence.HasCaptured(Expected(), Session(), Intent()).Should().BeTrue();
        AccountStripeEvidence.HasCaptured(Expected(), Session(), Intent() with { ReceivedMinor = 999 }).Should().BeFalse();
        AccountStripeEvidence.HasCaptured(Expected(), Session(), Intent() with { ChargeId = null }).Should().BeFalse();
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("requires_payment_method")]
    [InlineData("requires_action")]
    [InlineData("requires_confirmation")]
    [InlineData("requires_capture")]
    [InlineData("new_provider_state")]
    public void Incomplete_and_unknown_states_neither_capture_nor_release(string status)
    {
        var session = Session() with { Status = "expired", PaymentStatus = "unpaid" };
        var intent = Intent() with { Status = status, ReceivedMinor = 0 };
        AccountStripeEvidence.HasCaptured(Expected(), session, intent).Should().BeFalse();
        AccountStripeEvidence.CanRelease(Expected(), session, intent).Should().BeFalse();
    }

    [Fact]
    public void Expiry_alone_cannot_release_a_provider_intent()
    {
        var session = Session() with { Status = "expired", PaymentStatus = "unpaid" };
        AccountStripeEvidence.CanRelease(Expected(), session, null).Should().BeFalse();
        AccountStripeEvidence.CanRelease(Expected(), session,
            Intent() with { Status = "canceled", ReceivedMinor = 0 }).Should().BeTrue();
        AccountStripeEvidence.CanRelease(Expected(), Session(),
            Intent() with { Status = "canceled", ReceivedMinor = 0 }).Should().BeFalse();
    }

    [Fact]
    public void Expired_unpaid_session_without_an_intent_can_release()
    {
        var expected = Expected() with { IntentId = null };
        var session = Session() with { Status = "expired", PaymentStatus = "unpaid", IntentId = null };
        AccountStripeEvidence.CanRelease(expected, session, null).Should().BeTrue();
        AccountStripeEvidence.CanRelease(expected, session, Intent()).Should().BeFalse();
    }

    [Fact]
    public void Late_success_remains_captured_and_cannot_be_reallocated()
    {
        var session = Session() with { Status = "expired", PaymentStatus = "unpaid" };
        AccountStripeEvidence.HasCaptured(Expected(), session, Intent()).Should().BeTrue();
        AccountStripeEvidence.CanRelease(Expected(), session, Intent()).Should().BeFalse();
    }

    [Fact]
    public void Provider_identity_scope_and_frozen_amount_must_all_match()
    {
        var sessions = new[]
        {
            Session() with { Context = new AccountStripeContext("acct_other", false) },
            Session() with { Context = new AccountStripeContext("acct_evidence", true) },
            Session() with { Id = "cs_other" },
            Session() with { IntentId = "pi_other" },
            Session() with { AmountMinor = 999 },
            Session() with { Currency = "eur" },
            Session() with { ClientReferenceId = Guid.NewGuid().ToString("D") },
            Session() with { Metadata = new Dictionary<string, string>() }
        };
        foreach (var session in sessions)
        {
            var action = () => AccountStripeEvidence.HasCaptured(Expected(), session, Intent());
            action.Should().Throw<ConflictException>();
        }
    }

    [Fact]
    public void Intent_mismatch_does_not_inherit_a_trusted_session()
    {
        var intents = new[]
        {
            Intent() with { Context = new AccountStripeContext("acct_other", false) },
            Intent() with { Context = new AccountStripeContext("acct_evidence", true) },
            Intent() with { Id = "pi_other" },
            Intent() with { AmountMinor = 1001 },
            Intent() with { ReceivedMinor = 1001 },
            Intent() with { Currency = "eur" },
            Intent() with { Metadata = new Dictionary<string, string>() }
        };
        foreach (var intent in intents)
        {
            var action = () => AccountStripeEvidence.HasCaptured(Expected(), Session(), intent);
            action.Should().Throw<ConflictException>();
        }
    }
}
