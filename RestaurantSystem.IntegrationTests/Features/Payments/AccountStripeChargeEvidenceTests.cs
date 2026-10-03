using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountStripeChargeEvidenceTests
{
    private static readonly Guid AttemptId = Guid.Parse("163ce75e-7218-476b-af27-14d9aa144316");
    private static readonly AccountStripeContext Context = new("acct_charge", false);
    private static readonly IReadOnlyDictionary<string, string> Metadata = new Dictionary<string, string>
    {
        [AccountStripeCheckoutClient.AttemptMetadataKey] = AttemptId.ToString("D"),
        [AccountStripeCheckoutClient.SchemaMetadataKey] = AccountStripeCheckoutClient.SchemaVersion
    };

    private static AccountStripeExpectation Expected() => new()
    {
        AttemptId = AttemptId,
        Context = Context,
        AmountMinor = 334,
        Currency = "CHF",
        SessionId = "cs_charge",
        IntentId = "pi_charge"
    };

    private static AccountStripeSession Session() => new()
    {
        Id = "cs_charge",
        Context = Context,
        Status = "complete",
        PaymentStatus = "paid",
        AmountMinor = 334,
        Currency = "chf",
        IntentId = "pi_charge",
        ClientReferenceId = AttemptId.ToString("D"),
        Metadata = Metadata
    };

    private static AccountStripeIntent Intent() => new()
    {
        Id = "pi_charge",
        Context = Context,
        Status = "succeeded",
        AmountMinor = 334,
        ReceivedMinor = 334,
        Currency = "chf",
        ChargeId = "ch_charge",
        Metadata = Metadata
    };

    private static AccountStripeCharge Charge() => new()
    {
        Id = "ch_charge",
        Context = Context,
        IntentId = "pi_charge",
        Status = "succeeded",
        AmountMinor = 334,
        CapturedMinor = 334,
        RefundedMinor = 0,
        Currency = "chf",
        Paid = true,
        Captured = true,
        Disputed = false
    };

    [Fact]
    public void Only_canonical_unreversed_charge_proves_net_funds()
    {
        AccountStripeChargeEvidence.HasUnreversedCapture(Expected(), Session(), Intent(), Charge())
            .Should().BeTrue();
        AccountStripeChargeEvidence.HasUnreversedCapture(Expected(), Session(), Intent(),
            Charge() with { RefundedMinor = 334 }).Should().BeFalse();
        AccountStripeChargeEvidence.HasUnreversedCapture(Expected(), Session(), Intent(),
            Charge() with { RefundedMinor = 1 }).Should().BeFalse();
        AccountStripeChargeEvidence.HasUnreversedCapture(Expected(), Session(), Intent(),
            Charge() with { Disputed = true }).Should().BeFalse();
    }

    [Fact]
    public void Authorization_and_partial_capture_do_not_pay_the_frozen_allocation()
    {
        var unknown = new[]
        {
            Charge() with { Captured = false, CapturedMinor = 0 },
            Charge() with { CapturedMinor = 333 },
            Charge() with { Paid = false },
            Charge() with { Status = "pending" },
            Charge() with { Status = "future_state" }
        };
        foreach (var charge in unknown)
            AccountStripeChargeEvidence.HasUnreversedCapture(Expected(), Session(), Intent(), charge)
                .Should().BeFalse();
    }

    [Fact]
    public void Wrong_identity_or_invalid_refund_evidence_fails_closed()
    {
        var invalid = new[]
        {
            Charge() with { Id = "ch_other" },
            Charge() with { IntentId = "pi_other" },
            Charge() with { Context = new AccountStripeContext("acct_other", false) },
            Charge() with { Context = new AccountStripeContext("acct_charge", true) },
            Charge() with { AmountMinor = 335 },
            Charge() with { Currency = "eur" },
            Charge() with { CapturedMinor = 335 },
            Charge() with { CapturedMinor = -1 },
            Charge() with { RefundedMinor = -1 },
            Charge() with { RefundedMinor = 335 }
        };
        foreach (var charge in invalid)
        {
            var action = () => AccountStripeChargeEvidence.HasUnreversedCapture(
                Expected(), Session(), Intent(), charge);
            action.Should().Throw<ConflictException>();
        }
    }
}
