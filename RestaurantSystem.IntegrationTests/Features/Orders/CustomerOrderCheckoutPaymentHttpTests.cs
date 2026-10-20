using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Features.Basket;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCheckoutPaymentHttpTests(DatabaseFixture database) : CustomerOrderCheckoutJournalFixture(database)
{
    private const string Path = "/api/customer-mobile/checkout/payment/recover";
    private readonly Mock<IStripeCheckoutClient> _provider = new(MockBehavior.Strict);
    private readonly Mock<IStripeGateway> _gateway = new(MockBehavior.Strict);
    private StripeCheckoutSession? _remote;

    private TestWebApplicationFactory Factory(ICustomerOrderCheckoutEffects? effects = null)
    {
        _gateway.SetupGet(value => value.IsConfigured).Returns(true);
        _gateway.SetupGet(value => value.ConnectedAccountId).Returns("acct_native_fixture");
        _provider.Setup(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckoutSessionRequest request, CancellationToken _) =>
                _remote = CustomerOrderCheckoutLeaseTests.Evidence(request));
        _provider.Setup(value => value.GetAsync("cs_native_canonical_fixture", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _remote);
        _provider.Setup(value => value.GetPaymentIntentAsync("pi_native_settlement_fixture", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _remote?.NativeIntentEvidence);
        return new(Database.ConnectionString, configureTestServices: services =>
        {
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
            });
            services.PostConfigure<CustomerMobileSettings>(value =>
            {
                value.Enabled = true;
                value.TenantId = Guid.NewGuid();
                value.Lifecycle = "active";
                value.EnabledCapabilities = ["menu", "ordering", "accounts", "online-payments"];
            });
            services.PostConfigure<CustomerHostedCheckoutSettings>(value =>
            {
                value.Enabled = true;
                value.LinkOrigin = Settings.LinkOrigin;
                value.TenantSlug = Settings.TenantSlug;
                value.HostedPageOrigins = Settings.HostedPageOrigins;
            });
            services.PostConfigure<StripeSettings>(value => value.PlatformApiKey = "rk_test_native_fixture"); // pragma: allowlist secret
            services.RemoveAll<IStripeCheckoutClient>();
            services.AddSingleton(_provider.Object);
            services.RemoveAll<IStripeGateway>();
            services.AddSingleton(_gateway.Object);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<ICustomerOrderCheckoutEffects>();
            services.AddSingleton(effects ?? Mock.Of<ICustomerOrderCheckoutEffects>());
        }, disableApplicationHostedServices: true);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient client, object? proof = null, string? session = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(proof ?? Proof) };
        request.Headers.Add("X-Session-Id", session ?? Session);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Guest_receives_only_a_noncacheable_original_payment_decision_and_paid_recovery_records_money_once()
    {
        var orderId = await SeedAsync();
        await using var factory = Factory();
        using var client = factory.CreateClient();
        using var first = await SendAsync(client);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        first.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var ready = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var data = ready.RootElement.GetProperty("data");
        data.GetProperty("outcome").GetString().Should().Be("ready");
        data.GetProperty("operationId").GetGuid().Should().Be(Operation);
        data.GetProperty("orderId").GetGuid().Should().Be(orderId);
        data.GetProperty("checkoutUrl").GetString().Should().Be("https://checkout.stripe.com/fixture");
        var keys = data.EnumerateObject().Select(value => value.Name).ToArray();
        keys.Should().BeEquivalentTo("schemaVersion", "operationId", "orderId", "outcome", "checkoutUrl", "expiresAt");
        var request = (await FreezeAsync());
        _remote = CustomerOrderCheckoutSettlementTests.Paid(new(Guid.NewGuid(),
            CustomerOrderCheckoutReplay.Read(request, Clock.Now.UtcDateTime, forCreate: false), _remote!.Id,
            Clock.Now.UtcDateTime.AddMinutes(1), request.MaximumCreateRetryAt));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await SendAsync(client);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            using var paid = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            paid.RootElement.GetProperty("data").GetProperty("outcome").GetString().Should().Be("paid");
            (await response.Content.ReadAsStringAsync()).Should().NotContain("pi_native_settlement_fixture")
                .And.NotContain("acct_native_fixture").And.NotContain("native_order_operation").And.NotContain("payloadHash");
        }
        await using var read = Database.CreateContext();
        (await read.OrderPayments.CountAsync()).Should().Be(1);
        (await read.OrderCheckoutSessions.CountAsync()).Should().Be(1);
        (await read.Orders.SingleAsync()).TotalPaid.Should().Be(12.34m);
        _provider.Verify(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Original_proof_cannot_be_borrowed_by_staff_customers_or_failed_authentication_and_extra_fields_are_rejected()
    {
        await SeedAsync();
        await using var factory = Factory();
        using var client = factory.CreateClient();
        foreach (var role in new[] { UserRole.Admin, UserRole.Server, UserRole.Cashier, UserRole.KitchenStaff, UserRole.Customer })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CustomerBasketTokens.Create(factory.Services, role));
            using var response = await SendAsync(client);
            response.StatusCode.Should().Be(role == UserRole.Customer ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden);
        }
        foreach (var token in new[] { "malformed-token", CustomerBasketTokens.Create(factory.Services, UserRole.Customer, expired: true) })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await SendAsync(client);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        client.DefaultRequestHeaders.Authorization = null;
        using (var wrong = await SendAsync(client, session: Guid.NewGuid().ToString("D"))) wrong.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var changed = await SendAsync(client, Proof with { PayloadHash = new string('b', 64) })) changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var extra = await SendAsync(client, new { Proof.SchemaVersion, Proof.OperationId, Proof.PayloadHash, amount = 1 }))
            extra.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _provider.Verify(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _provider.Verify(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        using (var positive = await SendAsync(client)) positive.StatusCode.Should().Be(HttpStatusCode.OK, await positive.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Current_customer_can_recover_but_database_role_withdrawal_cannot_be_overridden_by_a_valid_old_token()
    {
        var owner = Guid.NewGuid();
        await SeedAsync(owner);
        await using var factory = Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            CustomerBasketTokens.Create(factory.Services, UserRole.Customer, userId: owner.ToString("D")));
        using (var positive = await SendAsync(client)) positive.StatusCode.Should().Be(HttpStatusCode.OK, await positive.Content.ReadAsStringAsync());
        await using (var write = Database.CreateContext())
        {
            (await write.Users.SingleAsync(value => value.Id == owner)).Role = UserRole.Admin;
            await write.SaveChangesAsync();
        }
        using var withdrawn = await SendAsync(client);
        withdrawn.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _provider.Verify(value => value.CreateAsync(It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("http://checkout.stripe.com/fixture")]
    [InlineData("https://checkout.stripe.com.attacker.test/fixture")]
    [InlineData("https://attacker.test/fixture")]
    [InlineData("https://user@checkout.stripe.com/fixture")]
    [InlineData("https://checkout.stripe.com:444/fixture")]
    [InlineData("https://checkout.stripe.com/\\fixture")]
    [InlineData("https://checkout.stripe.com/fixture\n")]
    public async Task Hostile_hosted_pages_never_become_a_customer_navigation_offer(string url)
    {
        await SeedAsync();
        await using var factory = Factory();
        using var client = factory.CreateClient();
        using (var positive = await SendAsync(client)) positive.StatusCode.Should().Be(HttpStatusCode.OK);
        _remote = _remote! with { Url = url };
        using var response = await SendAsync(client);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("outcome").GetString().Should().Be("pending");
        data.GetProperty("checkoutUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Ownership_withdrawal_during_effects_blocks_private_response_after_preserving_the_capture()
    {
        var owner = Guid.NewGuid();
        await SeedAsync(owner);
        var effects = new Mock<ICustomerOrderCheckoutEffects>(MockBehavior.Strict);
        effects.Setup(value => value.CompleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(async () =>
        {
            await using var write = Database.CreateContext();
            await write.Users.Where(value => value.Id == owner).ExecuteUpdateAsync(update => update.SetProperty(value => value.Role, UserRole.Admin));
        });
        await using var factory = Factory(effects.Object);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            CustomerBasketTokens.Create(factory.Services, UserRole.Customer, userId: owner.ToString("D")));
        using (var ready = await SendAsync(client)) ready.StatusCode.Should().Be(HttpStatusCode.OK, await ready.Content.ReadAsStringAsync());
        var journal = await FreezeAsync(owner);
        _remote = CustomerOrderCheckoutSettlementTests.Paid(new(Guid.NewGuid(),
            CustomerOrderCheckoutReplay.Read(journal, Clock.Now.UtcDateTime, forCreate: false), _remote!.Id,
            Clock.Now.UtcDateTime.AddMinutes(1), journal.MaximumCreateRetryAt));
        using var response = await SendAsync(client);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().NotContain("pi_native_settlement_fixture");
        await using var read = Database.CreateContext();
        (await read.Orders.SingleAsync()).TotalPaid.Should().Be(12.34m);
        (await read.OrderPayments.CountAsync()).Should().Be(1);
    }
}
