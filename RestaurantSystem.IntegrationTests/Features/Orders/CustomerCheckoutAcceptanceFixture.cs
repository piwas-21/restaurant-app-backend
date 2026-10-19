using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public abstract class CustomerCheckoutAcceptanceFixture(DatabaseFixture fixture) : CustomerCheckoutFixture(fixture)
{
    protected readonly Mock<IOrderPostCreationService> Notifications = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<IOrderPostCreationService>();
        services.AddSingleton(Notifications.Object);
    }

    protected async Task<CustomerCheckoutIntentDto> IntentAsync(OrderType type = OrderType.Takeaway,
        PaymentMethod method = PaymentMethod.Cash)
    {
        var basket = await CheckoutBasketAsync(type);
        var review = Review(basket);
        var quote = await QuoteAsync(review);
        return new()
        {
            Review = review,
            ExpectedQuoteFingerprint = quote.QuoteFingerprint,
            Contact = new() { Name = "Native fixture", Email = "native@example.test", Phone = "+41000000000" },
            PaymentMethod = method,
            Address = type == OrderType.Delivery ? new()
            {
                Street = "Fixture street",
                City = "Fixture city",
                PostalCode = "1000"
            } : null
        };
    }

    protected async Task<CustomerCheckoutSubmissionDto> PreparedAsync(CustomerCheckoutIntentDto intent)
    {
        var response = await SendAsync("/api/customer-mobile/checkout/prepare", intent);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var data = (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerCheckoutPreparationDto>>(JsonOptions))!.Data!;
        data.PayloadHash.Should().MatchRegex("^[a-f0-9]{64}$");
        return new() { SchemaVersion = 1, OperationId = Guid.NewGuid(), PayloadHash = data.PayloadHash, Intent = intent };
    }

    protected Task<HttpResponseMessage> SubmitAsync(CustomerCheckoutSubmissionDto submission, string? session = null) =>
        SendAsync("/api/customer-mobile/checkout/submit", submission, session);

    protected Task<HttpResponseMessage> ResolveAsync(CustomerCheckoutSubmissionDto submission, bool cancel = false,
        string? session = null) => SendAsync($"/api/customer-mobile/checkout/{(cancel ? "cancel" : "recover")}",
        new CustomerOrderRecoveryDto { SchemaVersion = 1, OperationId = submission.OperationId, PayloadHash = submission.PayloadHash }, session);

    protected static async Task<CustomerOrderResolutionDto> OrderResolutionAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("quickActionToken").And.NotContain("customerEmail").And.NotContain("guestStatusToken");
        return (await response.Content.ReadFromJsonAsync<ApiResponse<CustomerOrderResolutionDto>>(JsonOptions))!.Data!;
    }
}
