using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Settings;
using Stripe;
using Stripe.Checkout;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountStripeCheckoutClientTests
{
    private static readonly Guid AttemptId = Guid.Parse("57b60e3e-ebec-40bc-a4e3-f31ee46e7efb");
    private const string Account = "acct_transport";

    private static AccountStripeCheckoutRequest Request() => new()
    {
        AttemptId = AttemptId,
        Context = new AccountStripeContext(Account, false),
        AmountMinor = 334,
        Currency = "CHF",
        ExpiresAt = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
        IdempotencyKey = "account-attempt-create-v1"
    };

    private static (AccountStripeCheckoutClient Client, Mock<IStripeClient> Transport) Create(
        bool configured = true, string returnPath = "/table-account")
    {
        var settings = new StripeSettings
        {
            Enabled = configured,
            PlatformApiKey = "rk_test_transport", // pragma: allowlist secret
            ConnectedAccountId = Account
        };
        var credential = new StripeGateway(Options.Create(settings));
        var transport = new Mock<IStripeClient>(MockBehavior.Strict);
        var gateway = new Mock<IStripeGateway>();
        gateway.SetupGet(value => value.IsConfigured).Returns(credential.IsConfigured);
        gateway.SetupGet(value => value.ConnectedAccountId).Returns(Account);
        gateway.SetupGet(value => value.Client).Returns(transport.Object);
        gateway.Setup(value => value.BuildRequestOptions(It.IsAny<string>()))
            .Returns((string? key) => credential.BuildRequestOptions(key));
        var client = new AccountStripeCheckoutClient(
            gateway.Object,
            Options.Create(settings),
            Options.Create(new EmailSettings { FrontendBaseUrl = "https://tenant.test" }),
            Options.Create(new AccountCheckoutSettings { ReturnPath = returnPath }));
        return (client, transport);
    }

    [Fact]
    public async Task Sends_frozen_contribution_once_with_no_order_or_guest_identity()
    {
        var (client, transport) = Create();
        SessionCreateOptions? sent = null;
        RequestOptions? credentials = null;
        transport.Setup(value => value.RequestAsync<Session>(HttpMethod.Post, "/v1/checkout/sessions",
                It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .Callback<HttpMethod, string, BaseOptions, RequestOptions, CancellationToken>(
                (_, _, options, requestOptions, _) =>
                {
                    sent = (SessionCreateOptions)options;
                    credentials = requestOptions;
                })
            .ReturnsAsync(new Session
            {
                Id = "cs_transport",
                Url = "https://checkout.stripe.test/session",
                Livemode = false,
                Status = "open",
                PaymentStatus = "unpaid",
                AmountTotal = 334,
                Currency = "chf",
                ClientReferenceId = AttemptId.ToString("D")
            });

        var answer = await client.CreateAsync(Request(), CancellationToken.None);
        sent.Should().NotBeNull();
        sent!.ClientReferenceId.Should().Be(AttemptId.ToString("D"));
        sent.CustomerEmail.Should().BeNull();
        sent.Metadata.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["account_payment_attempt"] = AttemptId.ToString("D"),
            ["sofra_payment_schema"] = "account-payment-v1"
        });
        sent.PaymentIntentData.Metadata.Should().BeEquivalentTo(sent.Metadata);
        sent.PaymentIntentData.ApplicationFeeAmount.Should().BeNull();
        sent.PaymentMethodTypes.Should().BeNull();
        sent.LineItems.Should().HaveCount(1);
        sent.LineItems.Single().Quantity.Should().Be(1);
        sent.LineItems.Single().PriceData.UnitAmount.Should().Be(334);
        sent.LineItems.Single().PriceData.Currency.Should().Be("chf");
        sent.SuccessUrl.Should().Be($"https://tenant.test/table-account?paymentAttempt={AttemptId:D}&canceled=0");
        sent.CancelUrl.Should().Be($"https://tenant.test/table-account?paymentAttempt={AttemptId:D}&canceled=1");
        credentials!.StripeAccount.Should().Be(Account);
        credentials.IdempotencyKey.Should().Be("account-attempt-create-v1");
        answer.Context.Should().Be(new AccountStripeContext(Account, false));
        transport.VerifyAll();
    }

    [Fact]
    public async Task Account_or_environment_drift_never_creates_a_second_charge()
    {
        var (client, transport) = Create();
        var wrongAccount = Request() with { Context = new AccountStripeContext("acct_other", false) };
        var wrongEnvironment = Request() with { Context = new AccountStripeContext(Account, true) };
        await FluentActions.Invoking(() => client.CreateAsync(wrongAccount, CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();
        await FluentActions.Invoking(() => client.CreateAsync(wrongEnvironment, CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();
        transport.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("https://outside.test/return")]
    [InlineData("//outside.test/return")]
    [InlineData("/table-account?participant=secret")]
    [InlineData("/table-account#grant")]
    public async Task Unsafe_return_configuration_fails_before_provider_creation(string path)
    {
        var (client, transport) = Create(returnPath: path);
        await FluentActions.Invoking(() => client.CreateAsync(Request(), CancellationToken.None))
            .Should().ThrowAsync<BadRequestException>();
        transport.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Disabled_online_module_has_no_network_calls()
    {
        var (client, transport) = Create(configured: false);
        await FluentActions.Invoking(() => client.CreateAsync(Request(), CancellationToken.None))
            .Should().ThrowAsync<BadRequestException>();
        transport.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Canonical_charge_read_keeps_only_financial_evidence()
    {
        var (client, transport) = Create();
        transport.Setup(value => value.RequestAsync<Charge>(HttpMethod.Get, "/v1/charges/ch_transport",
                It.IsAny<BaseOptions>(), It.Is<RequestOptions>(options => options.StripeAccount == Account),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Charge
            {
                Id = "ch_transport",
                PaymentIntentId = "pi_transport",
                Livemode = false,
                Status = "succeeded",
                Amount = 334,
                AmountCaptured = 334,
                AmountRefunded = 100,
                Currency = "chf",
                Paid = true,
                Captured = true,
                Disputed = false
            });
        var charge = await client.GetChargeAsync("ch_transport", CancellationToken.None);
        charge.Should().BeEquivalentTo(new AccountStripeCharge
        {
            Id = "ch_transport",
            IntentId = "pi_transport",
            Context = new(Account, false),
            Status = "succeeded",
            AmountMinor = 334,
            CapturedMinor = 334,
            RefundedMinor = 100,
            Currency = "chf",
            Paid = true,
            Captured = true,
            Disputed = false
        });
        transport.VerifyAll();
    }

    [Fact]
    public async Task Only_resource_missing_becomes_unknown_evidence()
    {
        var (client, transport) = Create();
        transport.Setup(value => value.RequestAsync<Session>(HttpMethod.Get, "/v1/checkout/sessions/cs_missing",
                It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.NotFound,
                new StripeError { Code = "resource_missing" }, "missing"));
        (await client.GetAsync("cs_missing", CancellationToken.None)).Should().BeNull();
        transport.Setup(value => value.RequestAsync<Session>(HttpMethod.Get, "/v1/checkout/sessions/cs_revoked",
                It.IsAny<BaseOptions>(), It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StripeException(System.Net.HttpStatusCode.Unauthorized,
                new StripeError { Code = "api_key_expired" }, "revoked"));
        await FluentActions.Invoking(() => client.GetAsync("cs_revoked", CancellationToken.None))
            .Should().ThrowAsync<StripeException>();
    }
}
