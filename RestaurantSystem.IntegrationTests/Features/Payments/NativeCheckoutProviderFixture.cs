using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Settings;
using Stripe;
using Stripe.Checkout;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public abstract class NativeCheckoutProviderFixture
{
    protected static readonly Guid OrderId = Guid.Parse("16452fa7-1b29-4b70-807f-7a7d6ff7791f");
    protected static readonly Guid OperationId = Guid.Parse("be9ba23c-c6ee-4d0b-8dd1-7c53cd898bb7");
    protected const string Account = "acct_native_fixture";
    protected static readonly DateTime Expiry = new(2026, 10, 19, 20, 0, 0, DateTimeKind.Utc);
    protected static readonly string Return = $"https://links.example.test/r/fixture/payment-return/{OrderId:D}?operation={OperationId:D}";

    protected static CheckoutSessionRequest Request(long? fee = null) => new()
    {
        OrderId = OrderId,
        OrderNumber = "NATIVE-0001",
        Currency = "chf",
        AmountMinor = 1234,
        ApplicationFeeMinor = fee,
        ExpiresAt = Expiry,
        IdempotencyKey = $"sofra:native-order:v1:{OperationId:D}",
        NativeRouting = new(OperationId, new(Account, false), Return)
    };

    protected static Session Canonical() => new()
    {
        Id = "cs_native_fixture",
        Url = "https://checkout.example.test/native-fixture",
        Status = "open",
        PaymentStatus = "unpaid",
        AmountTotal = 1234,
        Currency = "chf",
        Livemode = false,
        ClientReferenceId = OrderId.ToString("D"),
        Metadata = new()
        {
            ["native_order_operation"] = OperationId.ToString("D"),
            ["sofra_payment_schema"] = "native-order-v1"
        }
    };

    protected static (StripeCheckoutClient Client, Mock<IStripeClient> Transport) Create(
        bool enabled = true, string key = "rk_test_native_fixture") // pragma: allowlist secret
    {
        var settings = new StripeSettings
        {
            Enabled = enabled,
            PlatformApiKey = key,
            ConnectedAccountId = Account,
            SuccessPath = "/new-web-success",
            CancelPath = "/new-web-cancel"
        };
        var credential = new StripeGateway(Options.Create(settings));
        var transport = new Mock<IStripeClient>(MockBehavior.Strict);
        var gateway = new Mock<IStripeGateway>();
        gateway.SetupGet(value => value.IsConfigured).Returns(credential.IsConfigured);
        gateway.SetupGet(value => value.ConnectedAccountId).Returns(Account);
        gateway.SetupGet(value => value.Client).Returns(transport.Object);
        gateway.Setup(value => value.BuildRequestOptions(It.IsAny<string>()))
            .Returns((string? replayKey) => credential.BuildRequestOptions(replayKey));
        return (new StripeCheckoutClient(gateway.Object, Options.Create(settings),
            Options.Create(new EmailSettings { FrontendBaseUrl = "https://changed-tenant.example.test" })), transport);
    }
}
