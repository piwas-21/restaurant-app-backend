using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class NativeTableCheckoutEvidenceTests
{
    private static readonly Guid Attempt = Guid.NewGuid();
    private static readonly NativeAccountCheckoutScope Native = new(Guid.NewGuid(), Guid.NewGuid());
    private static AccountStripeExpectation Expected => new()
    { AttemptId = Attempt, AmountMinor = 1400, Currency = "CHF", Context = new("acct_native", false), NativeScope = Native };
    private static AccountStripeSession Session => new()
    {
        Id = "cs_original",
        Context = Expected.Context,
        Status = "complete",
        PaymentStatus = "paid",
        AmountMinor = 1400,
        Currency = "chf",
        IntentId = "pi_original",
        ClientReferenceId = Attempt.ToString("D"),
        Metadata = Native.Metadata(Attempt)
    };
    private static AccountStripeIntent Intent => new()
    {
        Id = "pi_original",
        Context = Expected.Context,
        Status = "succeeded",
        AmountMinor = 1400,
        ReceivedMinor = 1400,
        Currency = "chf",
        ChargeId = "ch_original",
        Metadata = Native.Metadata(Attempt)
    };

    [Theory]
    [InlineData("tenant")]
    [InlineData("operation")]
    [InlineData("schema")]
    [InlineData("attempt")]
    [InlineData("merchant")]
    [InlineData("mode")]
    [InlineData("amount")]
    [InlineData("currency")]
    public void Both_session_and_intent_must_match_the_original_native_tuple(string field)
    {
        AccountStripeEvidence.HasCaptured(Expected, Session, Intent).Should().BeTrue();
        var metadata = Native.Metadata(Attempt);
        if (field == "tenant") metadata[NativeAccountCheckoutScope.TenantMetadataKey] = Guid.NewGuid().ToString("D");
        if (field == "operation") metadata[NativeAccountCheckoutScope.OperationMetadataKey] = Guid.NewGuid().ToString("D");
        if (field == "schema") metadata[AccountStripeCheckoutClient.SchemaMetadataKey] = AccountStripeCheckoutClient.SchemaVersion;
        if (field == "attempt") metadata[AccountStripeCheckoutClient.AttemptMetadataKey] = Guid.NewGuid().ToString("D");
        var session = Session with
        {
            Metadata = metadata,
            Context = field == "merchant" ? new("acct_wrong", false) : field == "mode" ? new("acct_native", true) : Expected.Context,
            AmountMinor = field == "amount" ? 1401 : 1400,
            Currency = field == "currency" ? "eur" : "chf"
        };
        FluentActions.Invoking(() => AccountStripeEvidence.HasCaptured(Expected, session, Intent)).Should().Throw<ConflictException>();
        var intent = Intent with
        {
            Metadata = metadata,
            Context = session.Context,
            AmountMinor = session.AmountMinor!.Value,
            Currency = session.Currency!
        };
        FluentActions.Invoking(() => AccountStripeEvidence.HasCaptured(Expected, Session, intent)).Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData("open", "unpaid")]
    [InlineData("complete", "unpaid")]
    [InlineData("expired", "unpaid")]
    [InlineData("open", "paid")]
    public void Captured_intent_with_contradictory_session_never_proves_native_settlement(string status, string payment)
    {
        AccountStripeEvidence.HasCaptured(Expected, Session with { Status = status, PaymentStatus = payment }, Intent).Should().BeFalse();
        AccountStripeEvidence.CanRelease(Expected, Session with { Status = status, PaymentStatus = payment }, Intent).Should().BeFalse();
    }
}
