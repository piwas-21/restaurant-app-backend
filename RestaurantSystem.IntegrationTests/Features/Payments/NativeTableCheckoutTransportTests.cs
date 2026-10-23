using FluentAssertions;
using Moq;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using Stripe;
using Stripe.Checkout;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed partial class AccountStripeCheckoutClientTests
{
    [Fact]
    public async Task Native_transport_uses_frozen_attempt_return_and_both_native_metadata_copies()
    {
        var (client, transport) = Create(returnPath: "/changed-web-return");
        var native = new NativeAccountCheckoutScope(Guid.NewGuid(), Guid.NewGuid());
        var request = Request() with
        {
            NativeScope = native,
            ReturnBaseUrl = native.ReturnBase("https://links.example.test", "fixture", AttemptId)
        };
        SessionCreateOptions? sent = null;
        transport.Setup(value => value.RequestAsync<Session>(HttpMethod.Post, "/v1/checkout/sessions",
            It.IsAny<BaseOptions>(), It.Is<RequestOptions>(options => options.StripeAccount == Account
                && options.IdempotencyKey == request.IdempotencyKey), It.IsAny<CancellationToken>()))
            .Callback<HttpMethod, string, BaseOptions, RequestOptions, CancellationToken>((_, _, options, _, _) => sent = (SessionCreateOptions)options)
            .ReturnsAsync(new Session
            {
                Id = "cs_native_transport",
                Status = "open",
                PaymentStatus = "unpaid",
                Livemode = false,
                AmountTotal = 334,
                Currency = "chf",
                ClientReferenceId = AttemptId.ToString("D"),
                Metadata = native.Metadata(AttemptId)
            });
        await client.CreateAsync(request, CancellationToken.None);
        sent.Should().NotBeNull();
        sent!.SuccessUrl.Should().Be($"https://links.example.test/r/fixture/table-payment-return/{AttemptId:D}?operation={native.OperationId:D}");
        sent.CancelUrl.Should().Be(sent.SuccessUrl);
        sent.Metadata.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["account_payment_attempt"] = AttemptId.ToString("D"),
            ["sofra_payment_schema"] = "native-table-payment-v1",
            ["native_table_tenant"] = native.TenantId.ToString("D"),
            ["native_table_operation"] = native.OperationId.ToString("D")
        });
        sent.PaymentIntentData.Metadata.Should().BeEquivalentTo(sent.Metadata);
        sent.CustomerEmail.Should().BeNull(); sent.ClientReferenceId.Should().Be(AttemptId.ToString("D"));
        sent.ExpiresAt.Should().Be(request.ExpiresAt); transport.VerifyAll(); transport.VerifyNoOtherCalls();
    }
}
