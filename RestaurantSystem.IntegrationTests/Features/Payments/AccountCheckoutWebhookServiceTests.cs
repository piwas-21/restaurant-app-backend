using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;
using Stripe;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed class AccountCheckoutWebhookServiceTests
{
    private static readonly string Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private const string ConnectedAccount = "acct_account_checkout_test";

    [Fact]
    public async Task Missing_secret_is_unavailable_without_reading_provider_context_or_scheduling()
    {
        var provider = new Mock<IAccountStripeCheckoutClient>(MockBehavior.Strict);
        var leases = new Mock<IAccountCheckoutLeaseStore>(MockBehavior.Strict);
        var service = CreateService(provider, leases, string.Empty);

        var result = await service.HandleAsync("{}", null, CancellationToken.None);

        result.Should().Be(AccountCheckoutWebhookDisposition.NotConfigured);
        provider.VerifyNoOtherCalls();
        leases.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Invalid_signature_never_reads_provider_context_or_schedules()
    {
        var provider = new Mock<IAccountStripeCheckoutClient>(MockBehavior.Strict);
        var leases = new Mock<IAccountCheckoutLeaseStore>(MockBehavior.Strict);
        var payload = EventPayload("payment_intent.succeeded", ConnectedAccount, false,
            PaymentIntentObject(Guid.NewGuid()));
        var service = CreateService(provider, leases, Secret);

        var result = await service.HandleAsync(payload, "t=1,v1=invalid", CancellationToken.None);

        result.Should().Be(AccountCheckoutWebhookDisposition.Invalid);
        provider.VerifyNoOtherCalls();
        leases.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Valid_signature_over_invalid_json_is_rejected_without_provider_access()
    {
        var provider = new Mock<IAccountStripeCheckoutClient>(MockBehavior.Strict);
        var leases = new Mock<IAccountCheckoutLeaseStore>(MockBehavior.Strict);
        const string payload = "not-json";

        var result = await CreateService(provider, leases, Secret)
            .HandleAsync(payload, Sign(payload), CancellationToken.None);

        result.Should().Be(AccountCheckoutWebhookDisposition.Invalid);
        provider.VerifyNoOtherCalls();
        leases.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("acct_wrong", false)]
    [InlineData(ConnectedAccount, true)]
    public async Task Account_or_livemode_mismatch_is_rejected(string account, bool liveMode)
    {
        var provider = Provider();
        var leases = new Mock<IAccountCheckoutLeaseStore>(MockBehavior.Strict);
        var payload = EventPayload("payment_intent.succeeded", account, liveMode,
            PaymentIntentObject(Guid.NewGuid()));
        var service = CreateService(provider, leases, Secret);

        var result = await service.HandleAsync(payload, Sign(payload), CancellationToken.None);

        result.Should().Be(AccountCheckoutWebhookDisposition.Invalid);
        leases.VerifyNoOtherCalls();
        provider.Verify(value => value.ReadContext(), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Signed_payment_intent_event_schedules_only_attempt_and_provider_identifiers()
    {
        var attemptId = Guid.NewGuid();
        var provider = Provider();
        var leases = new Mock<IAccountCheckoutLeaseStore>(MockBehavior.Strict);
        leases.Setup(value => value.ScheduleWebhookWakeupAsync(
            It.Is<AccountCheckoutWebhookReferences>(references => references.AttemptId == attemptId
                && references.IntentId == "pi_account_checkout"
                && references.ChargeId == "ch_account_checkout" && references.SessionId == null),
            new AccountStripeContext(ConnectedAccount, false), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var payload = EventPayload("payment_intent.succeeded", ConnectedAccount, false,
            PaymentIntentObject(attemptId));
        var service = CreateService(provider, leases, Secret);

        var result = await service.HandleAsync(payload, Sign(payload), CancellationToken.None);

        result.Should().Be(AccountCheckoutWebhookDisposition.Accepted);
        provider.Verify(value => value.ReadContext(), Times.Once);
        provider.VerifyNoOtherCalls();
        leases.VerifyAll();
        leases.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Checkout_session_client_reference_and_object_ids_can_wake_before_metadata_is_saved()
    {
        var attemptId = Guid.NewGuid();
        var provider = Provider();
        var leases = new Mock<IAccountCheckoutLeaseStore>(MockBehavior.Strict);
        leases.Setup(value => value.ScheduleWebhookWakeupAsync(
            It.Is<AccountCheckoutWebhookReferences>(references => references.AttemptId == attemptId
                && references.SessionId == "cs_account_checkout" && references.IntentId == "pi_account_checkout"),
            new AccountStripeContext(ConnectedAccount, false), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var session = JsonSerializer.Serialize(new
        {
            @object = "checkout.session",
            id = "cs_account_checkout",
            client_reference_id = attemptId.ToString("D"),
            payment_intent = "pi_account_checkout"
        });
        var payload = EventPayload("checkout.session.completed", ConnectedAccount, false, session);

        var result = await CreateService(provider, leases, Secret)
            .HandleAsync(payload, Sign(payload), CancellationToken.None);

        result.Should().Be(AccountCheckoutWebhookDisposition.Accepted);
        leases.VerifyAll();
        leases.VerifyNoOtherCalls();
        provider.Verify(value => value.ReadContext(), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Charge_event_without_attempt_metadata_can_correlate_by_saved_charge_and_intent_ids()
    {
        var provider = Provider();
        var leases = new Mock<IAccountCheckoutLeaseStore>(MockBehavior.Strict);
        leases.Setup(value => value.ScheduleWebhookWakeupAsync(
            It.Is<AccountCheckoutWebhookReferences>(references => references.AttemptId == null
                && references.IntentId == "pi_saved" && references.ChargeId == "ch_saved"),
            new AccountStripeContext(ConnectedAccount, false), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var charge = JsonSerializer.Serialize(new
        {
            @object = "charge",
            id = "ch_saved",
            payment_intent = "pi_saved",
            amount_refunded = 334,
            currency = "chf",
            refunded = true
        });
        var payload = EventPayload("charge.refunded", ConnectedAccount, false, charge);

        var result = await CreateService(provider, leases, Secret)
            .HandleAsync(payload, Sign(payload), CancellationToken.None);

        result.Should().Be(AccountCheckoutWebhookDisposition.Accepted);
        leases.VerifyAll();
        leases.VerifyNoOtherCalls();
        provider.Verify(value => value.ReadContext(), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Signed_unrelated_event_is_acknowledged_without_scheduling()
    {
        var provider = Provider();
        var leases = new Mock<IAccountCheckoutLeaseStore>(MockBehavior.Strict);
        var payload = EventPayload("customer.updated", ConnectedAccount, false,
            "{\"object\":\"customer\",\"id\":\"cus_unrelated\"}");

        var result = await CreateService(provider, leases, Secret)
            .HandleAsync(payload, Sign(payload), CancellationToken.None);

        result.Should().Be(AccountCheckoutWebhookDisposition.Accepted);
        leases.VerifyNoOtherCalls();
        provider.Verify(value => value.ReadContext(), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Missing_connected_account_configuration_is_unavailable()
    {
        var provider = new Mock<IAccountStripeCheckoutClient>(MockBehavior.Strict);
        provider.Setup(value => value.ReadContext()).Throws(new BadRequestException("unconfigured"));
        var leases = new Mock<IAccountCheckoutLeaseStore>(MockBehavior.Strict);
        var payload = EventPayload("payment_intent.succeeded", ConnectedAccount, false,
            PaymentIntentObject(Guid.NewGuid()));

        var result = await CreateService(provider, leases, Secret)
            .HandleAsync(payload, Sign(payload), CancellationToken.None);

        result.Should().Be(AccountCheckoutWebhookDisposition.NotConfigured);
        provider.Verify(value => value.ReadContext(), Times.Once);
        provider.VerifyNoOtherCalls();
        leases.VerifyNoOtherCalls();
    }

    private static AccountCheckoutWebhookService CreateService(Mock<IAccountStripeCheckoutClient> provider,
        Mock<IAccountCheckoutLeaseStore> leases, string secret) => new(
        Options.Create(new AccountCheckoutWebhookSettings { SigningSecret = secret }),
        provider.Object, leases.Object);

    private static Mock<IAccountStripeCheckoutClient> Provider()
    {
        var provider = new Mock<IAccountStripeCheckoutClient>(MockBehavior.Strict);
        provider.Setup(value => value.ReadContext()).Returns(new AccountStripeContext(ConnectedAccount, false));
        return provider;
    }

    private static string PaymentIntentObject(Guid attemptId) => JsonSerializer.Serialize(new
    {
        @object = "payment_intent",
        id = "pi_account_checkout",
        latest_charge = "ch_account_checkout",
        metadata = new Dictionary<string, string>
        {
            [AccountStripeCheckoutClient.AttemptMetadataKey] = attemptId.ToString("D"),
            [AccountStripeCheckoutClient.SchemaMetadataKey] = AccountStripeCheckoutClient.SchemaVersion
        },
        amount_received = 334,
        currency = "chf",
        status = "succeeded"
    });

    private static string EventPayload(string type, string account, bool liveMode, string providerObject)
    {
        using var parsedObject = JsonDocument.Parse(providerObject);
        return JsonSerializer.Serialize(new
        {
            id = "evt_account_checkout",
            @object = "event",
            type,
            account,
            livemode = liveMode,
            data = new { @object = parsedObject.RootElement }
        });
    }

    private static string Sign(string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = EventUtility.ComputeSignature(Secret, timestamp, payload);
        return $"t={timestamp},v1={signature}";
    }
}
