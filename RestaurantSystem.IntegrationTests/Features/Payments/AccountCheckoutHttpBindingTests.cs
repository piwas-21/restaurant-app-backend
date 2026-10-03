using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Stripe;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

[Collection("Database Lane 3")]
public sealed class AccountCheckoutHttpBindingTests(DatabaseFixture fixture)
{
    private const string WebhookRoute = "api/webhooks/stripe/account-checkouts";
    private const string ConnectedAccount = "acct_http_binding_test";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multiple_signature_headers_reach_real_verification_and_modified_payload_is_rejected(bool tamper)
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var provider = new Mock<IAccountStripeCheckoutClient>(MockBehavior.Strict);
        provider.Setup(value => value.ReadContext()).Returns(new AccountStripeContext(ConnectedAccount, false));
        var leases = new Mock<IAccountCheckoutLeaseStore>(MockBehavior.Strict);
        await using var factory = Factory(secret, provider, leases);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        var payload = JsonSerializer.Serialize(new
        {
            id = "evt_http_binding",
            @object = "event",
            type = "customer.updated",
            account = ConnectedAccount,
            livemode = false,
            data = new { @object = new { @object = "customer", id = "cus_http_binding" } }
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = EventUtility.ComputeSignature(secret, timestamp, payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookRoute);
        request.Headers.TryAddWithoutValidation("Stripe-Signature",
            new[] { $"t={timestamp},v1=invalid", $"v1={signature}" }).Should().BeTrue();
        request.Content = new StringContent(tamper ? payload + " " : payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(tamper ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        provider.Verify(value => value.ReadContext(), tamper ? Times.Never() : Times.Once());
        provider.VerifyNoOtherCalls();
        leases.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Combined_signature_header_limit_is_enforced_before_real_verification()
    {
        var provider = new Mock<IAccountStripeCheckoutClient>(MockBehavior.Strict);
        var leases = new Mock<IAccountCheckoutLeaseStore>(MockBehavior.Strict);
        await using var factory = Factory(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), provider, leases);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookRoute);
        request.Headers.TryAddWithoutValidation("Stripe-Signature",
            new[] { new string('a', 512), new string('b', 512) }).Should().BeTrue();
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        provider.VerifyNoOtherCalls();
        leases.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("/cancel")]
    public async Task Missing_expected_version_is_rejected_by_http_body_binding(string suffix)
    {
        using var client = fixture.SharedFactory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");
        var route = $"api/table-guest-visits/{Guid.NewGuid():D}/account-payments/operations/{Guid.NewGuid():D}/checkout{suffix}";
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri(route, UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("expectedVersion");
    }

    private TestWebApplicationFactory Factory(string secret, Mock<IAccountStripeCheckoutClient> provider,
        Mock<IAccountCheckoutLeaseStore> leases) => new(fixture.ConnectionString,
        new Dictionary<string, string> { [$"{AccountCheckoutWebhookSettings.SectionName}:SigningSecret"] = secret },
        services =>
        {
            services.RemoveAll<IAccountStripeCheckoutClient>();
            services.AddSingleton(provider.Object);
            services.RemoveAll<IAccountCheckoutLeaseStore>();
            services.AddSingleton(leases.Object);
        }, disableApplicationHostedServices: true);
}
