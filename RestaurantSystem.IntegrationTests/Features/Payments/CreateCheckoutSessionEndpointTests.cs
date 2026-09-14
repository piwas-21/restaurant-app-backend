using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

/// <summary>
/// The HTTP boundary for S4's payment-intent invariant. The handler test proves the policy in
/// isolation; this test proves the configured module and MVC pipeline cannot route an on-site order
/// to Stripe. The fake is verified for every operation, not only CreateAsync, so a pre-existing
/// checkout row cannot make a card-only order call GetAsync through the reuse path either.
/// </summary>
[Collection("Database Lane 2")]
public class CreateCheckoutSessionEndpointTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private Mock<IStripeCheckoutClient> _checkout = null!;
    private Guid _orderId;

    public CreateCheckoutSessionEndpointTests(DatabaseFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();

        _checkout = new Mock<IStripeCheckoutClient>(MockBehavior.Strict);
        _checkout.Setup(c => c.CreateAsync(
                It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeCheckoutSession
            {
                Id = "cs_test_unexpected",
                Url = "https://checkout.stripe.com/c/pay/cs_test_unexpected",
                Status = "open",
                PaymentStatus = "unpaid",
            });
        _checkout.Setup(c => c.GetAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeCheckoutSession?)null);
        _checkout.Setup(c => c.ExpireAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _factory = new TestWebApplicationFactory(
            _fixture.ConnectionString,
            new Dictionary<string, string>
            {
                ["Modules:Enforce"] = "true",
                ["Modules:Enabled"] = "core,online-payments",
                ["Stripe:Enabled"] = "true",
                ["Stripe:PlatformApiKey"] = "rk_test_checkout", // pragma: allowlist secret
                ["Stripe:ConnectedAccountId"] = "acct_test_checkout",
            },
            services =>
            {
                services.RemoveAll<IStripeCheckoutClient>();
                services.AddSingleton(_checkout.Object);
            });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousHeader, "true");

        await using var seed = _fixture.CreateContext();
        var order = new Order
        {
            OrderNumber = $"S4-API-{Guid.NewGuid():N}"[..14],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 10m,
            Total = 10m,
            CustomerEmail = "diner@example.com",
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CreateCheckoutSessionEndpointTests),
        };
        order.Payments.Add(new OrderPayment
        {
            PaymentMethod = PaymentMethod.CreditCard,
            Amount = 10m,
            Status = PaymentStatus.Pending,
            PaymentDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CreateCheckoutSessionEndpointTests),
        });
        seed.Orders.Add(order);
        await seed.SaveChangesAsync();
        seed.OrderCheckoutSessions.Add(new OrderCheckoutSession
        {
            OrderId = order.Id,
            SessionId = $"cs_test_existing_{Guid.NewGuid():N}",
            Status = CheckoutSessionStatus.Created,
            Currency = "chf",
            AmountMinor = 1000,
            IdempotencyKey = $"checkout:{order.Id}:1",
            ExpiresAt = DateTime.UtcNow.AddMinutes(31),
            ConnectedAccountId = "acct_test_checkout",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = nameof(CreateCheckoutSessionEndpointTests),
        });
        await seed.SaveChangesAsync();
        _orderId = order.Id;
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_on_site_card_order_is_refused_without_calling_stripe()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/payments/checkout-session", new { orderId = _orderId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("online payment intent");
        _checkout.Verify(c => c.CreateAsync(
            It.IsAny<CheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _checkout.Verify(c => c.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _checkout.Verify(c => c.ExpireAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        await using var verify = _fixture.CreateContext();
        (await verify.OrderCheckoutSessions.AsNoTracking().CountAsync())
            .Should().Be(1, "a refused order must not add or retire a checkout-session row");
    }
}
