using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using Stripe;
using Stripe.Checkout;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class NativeCheckoutEvidenceTests : NativeCheckoutProviderFixture
{
    private static StripeCheckoutSession Evidence() => new()
    {
        Id = "cs_native_fixture",
        Status = "open",
        PaymentStatus = "unpaid",
        AmountTotalMinor = 1234,
        Currency = "chf",
        ProviderContext = new(Account, false),
        ClientReferenceId = OrderId.ToString("D"),
        Metadata = Canonical().Metadata
    };

    [Theory]
    [InlineData("missing-context")]
    [InlineData("account")]
    [InlineData("mode")]
    [InlineData("currency")]
    [InlineData("missing-currency")]
    [InlineData("amount")]
    [InlineData("missing-amount")]
    [InlineData("order")]
    [InlineData("missing-order")]
    [InlineData("operation")]
    [InlineData("schema")]
    [InlineData("missing-metadata")]
    [InlineData("session")]
    [InlineData("empty-session")]
    public void Wrong_canonical_evidence_never_authorizes_native_session(string scenario)
    {
        FluentActions.Invoking(() => NativeCheckoutSessionEvidence.Require(Request(), Evidence())).Should().NotThrow();
        var wrong = scenario switch
        {
            "missing-context" => Evidence() with { ProviderContext = null },
            "account" => Evidence() with { ProviderContext = new("acct_other", false) },
            "mode" => Evidence() with { ProviderContext = new(Account, true) },
            "currency" => Evidence() with { Currency = "eur" },
            "missing-currency" => Evidence() with { Currency = null },
            "amount" => Evidence() with { AmountTotalMinor = 1235 },
            "missing-amount" => Evidence() with { AmountTotalMinor = null },
            "order" => Evidence() with { ClientReferenceId = OperationId.ToString("D") },
            "missing-order" => Evidence() with { ClientReferenceId = null },
            "operation" => Evidence() with
            {
                Metadata = new Dictionary<string, string>
                { ["native_order_operation"] = OrderId.ToString("D"), ["sofra_payment_schema"] = "native-order-v1" }
            },
            "schema" => Evidence() with
            {
                Metadata = new Dictionary<string, string>
                { ["native_order_operation"] = OperationId.ToString("D"), ["sofra_payment_schema"] = "account-payment-v1" }
            },
            "missing-metadata" => Evidence() with { Metadata = new Dictionary<string, string>() },
            "session" => Evidence() with { Id = "pi_native_fixture" },
            "empty-session" => Evidence() with { Id = string.Empty },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        FluentActions.Invoking(() => NativeCheckoutSessionEvidence.Require(Request(), wrong))
            .Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData("open", "unpaid")]
    [InlineData("complete", "unpaid")]
    [InlineData("complete", "paid")]
    [InlineData("expired", "unpaid")]
    [InlineData("future_status", "future_payment_status")]
    public void Identity_validation_does_not_guess_settlement_or_terminal_state(string status, string paymentStatus)
    {
        var value = Evidence() with { Status = status, PaymentStatus = paymentStatus };
        FluentActions.Invoking(() => NativeCheckoutSessionEvidence.Require(Request(), value)).Should().NotThrow();
        value.IsComplete.Should().Be(status == "complete");
        value.IsPaid.Should().Be(paymentStatus == "paid");
        value.IsExpired.Should().Be(status == "expired");
    }

    [Fact]
    public async Task Canonical_get_preserves_identity_without_creating_a_new_session()
    {
        var (client, transport) = Create();
        transport.Setup(value => value.RequestAsync<Session>(HttpMethod.Get, "/v1/checkout/sessions/cs_native_fixture",
            It.IsAny<BaseOptions>(), It.Is<RequestOptions>(options => options.StripeAccount == Account
                && options.IdempotencyKey == null), It.IsAny<CancellationToken>())).ReturnsAsync(Canonical());
        var value = await client.GetAsync("cs_native_fixture", CancellationToken.None);
        value.Should().NotBeNull();
        NativeCheckoutSessionEvidence.Require(Request(), value!);
        transport.VerifyAll();
        transport.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_result_with_wrong_currency_is_held_for_reconciliation()
    {
        var (client, transport) = Create();
        var remote = Canonical();
        remote.Currency = "eur";
        transport.Setup(value => value.RequestAsync<Session>(HttpMethod.Post, "/v1/checkout/sessions",
            It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(remote);
        await FluentActions.Invoking(() => client.CreateAsync(Request(), CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();
        transport.VerifyAll();
    }
}
