using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Services;
using Stripe;
using Stripe.Checkout;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class NativeCheckoutProviderTests : NativeCheckoutProviderFixture
{
    [Theory]
    [InlineData(null)]
    [InlineData(12L)]
    public async Task Frozen_routing_money_fee_and_key_survive_changed_web_configuration(long? fee)
    {
        var (client, transport) = Create();
        SessionCreateOptions? sent = null;
        RequestOptions? credentials = null;
        transport.Setup(value => value.RequestAsync<Session>(HttpMethod.Post, "/v1/checkout/sessions",
                It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<HttpMethod, string, BaseOptions, RequestOptions, CancellationToken>((_, _, options, auth, _) =>
            {
                sent = (SessionCreateOptions)options;
                credentials = auth;
            }).ReturnsAsync(Canonical());
        var request = Request(fee);
        var answer = await client.CreateAsync(request, CancellationToken.None);
        sent.Should().NotBeNull();
        sent!.SuccessUrl.Should().Be(Return);
        sent.CancelUrl.Should().Be(Return);
        sent.ExpiresAt.Should().Be(Expiry);
        sent.ClientReferenceId.Should().Be(OrderId.ToString("D"));
        sent.CustomerEmail.Should().BeNull();
        sent.PaymentMethodTypes.Should().BeNull();
        sent.Mode.Should().Be("payment");
        sent.Metadata.Should().BeEquivalentTo(Canonical().Metadata);
        sent.PaymentIntentData.Metadata.Should().BeEquivalentTo(Canonical().Metadata);
        sent.PaymentIntentData.ApplicationFeeAmount.Should().Be(fee);
        sent.LineItems.Should().HaveCount(1);
        sent.LineItems[0].Quantity.Should().Be(1);
        sent.LineItems[0].PriceData.UnitAmount.Should().Be(1234);
        sent.LineItems[0].PriceData.Currency.Should().Be("chf");
        sent.LineItems[0].PriceData.ProductData.Name.Should().Be("Order NATIVE-0001");
        credentials!.StripeAccount.Should().Be(Account);
        credentials.IdempotencyKey.Should().Be($"sofra:native-order:v1:{OperationId:D}");
        answer.ProviderContext.Should().Be(new StripeProviderContext(Account, false));
        answer.Currency.Should().Be("chf");
        answer.ClientReferenceId.Should().Be(OrderId.ToString("D"));
        answer.Metadata.Should().BeEquivalentTo(Canonical().Metadata);
        NativeCheckoutSessionEvidence.Require(request, answer);
        transport.VerifyAll();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(12L)]
    public async Task Legacy_web_request_retains_original_return_metadata_and_optional_fee_behavior(long? fee)
    {
        var (client, transport) = Create();
        SessionCreateOptions? sent = null;
        transport.Setup(value => value.RequestAsync<Session>(HttpMethod.Post, "/v1/checkout/sessions",
                It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<HttpMethod, string, BaseOptions, RequestOptions, CancellationToken>((_, _, options, _, _) =>
                sent = (SessionCreateOptions)options).ReturnsAsync(Canonical());
        await client.CreateAsync(Request(fee) with { NativeRouting = null }, CancellationToken.None);
        sent!.SuccessUrl.Should().Be($"https://changed-tenant.example.test/new-web-success?orderId={OrderId:D}&sessionId={{CHECKOUT_SESSION_ID}}");
        sent.CancelUrl.Should().Be($"https://changed-tenant.example.test/new-web-cancel?orderId={OrderId:D}&canceled=1");
        sent.Metadata.Should().BeNull();
        if (fee is null) sent.PaymentIntentData.Should().BeNull();
        else
        {
            sent.PaymentIntentData.ApplicationFeeAmount.Should().Be(fee);
            sent.PaymentIntentData.Metadata.Should().BeNull();
        }
        transport.VerifyAll();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("mode")]
    [InlineData("disabled")]
    [InlineData("unrecognized-key")]
    public async Task Provider_scope_drift_refuses_creation_before_transport(string scenario)
    {
        var (client, transport) = Create(enabled: scenario != "disabled",
            key: scenario == "unrecognized-key" ? "unrecognized" : "rk_test_native_fixture"); // pragma: allowlist secret
        var request = Request();
        if (scenario == "account") request = request with
        {
            NativeRouting = request.NativeRouting! with { Context = new("acct_other", false) }
        };
        if (scenario == "mode") request = request with
        {
            NativeRouting = request.NativeRouting! with { Context = new(Account, true) }
        };
        var invoke = () => client.CreateAsync(request, CancellationToken.None);
        if (scenario is "disabled" or "unrecognized-key") await invoke.Should().ThrowAsync<BadRequestException>();
        else await invoke.Should().ThrowAsync<ConflictException>();
        transport.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("http")]
    [InlineData("userinfo")]
    [InlineData("fragment")]
    [InlineData("extra-query")]
    [InlineData("duplicate-query")]
    [InlineData("wrong-order")]
    [InlineData("wrong-operation")]
    [InlineData("wrong-path")]
    [InlineData("encoded-path")]
    [InlineData("backslash")]
    [InlineData("custom-port")]
    public async Task Frozen_returns_reject_unexpected_or_secret_bearing_shapes(string scenario)
    {
        var (client, transport) = Create();
        var url = scenario switch
        {
            "http" => Return.Replace("https:", "http:", StringComparison.Ordinal),
            "userinfo" => Return.Replace("https://", "https://person:secret@", StringComparison.Ordinal), // pragma: allowlist secret -- synthetic rejection fixture
            "fragment" => Return + "#private",
            "extra-query" => Return + "&token=private",
            "duplicate-query" => Return + $"&operation={OperationId:D}",
            "wrong-order" => Return.Replace(OrderId.ToString("D"), Guid.Empty.ToString("D"), StringComparison.Ordinal),
            "wrong-operation" => Return.Replace(OperationId.ToString("D"), Guid.Empty.ToString("D"), StringComparison.Ordinal),
            "wrong-path" => Return.Replace("/payment-return/", "/order/", StringComparison.Ordinal),
            "encoded-path" => Return.Replace("/r/", "/%72/", StringComparison.Ordinal),
            "backslash" => Return.Replace("/r/", "\\r/", StringComparison.Ordinal),
            "custom-port" => Return.Replace(".test/", ".test:8443/", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var request = Request() with { NativeRouting = Request().NativeRouting! with { ReturnUrl = url } };
        await FluentActions.Invoking(() => client.CreateAsync(request, CancellationToken.None))
            .Should().ThrowAsync<BadRequestException>();
        transport.VerifyNoOtherCalls();
    }
}
