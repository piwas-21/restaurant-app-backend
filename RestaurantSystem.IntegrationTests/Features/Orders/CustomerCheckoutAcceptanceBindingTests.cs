using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutAcceptanceBindingTests(DatabaseFixture fixture) : CustomerCheckoutAcceptanceFixture(fixture)
{
    [Fact]
    public async Task Missing_and_unknown_submit_fields_cannot_bind_contact_payment_or_price_authority()
    {
        var submission = await PreparedAsync(await IntentAsync());
        foreach (var field in new[] { "schemaVersion", "operationId", "payloadHash", "intent" })
        {
            var body = JsonSerializer.SerializeToNode(submission, JsonOptions)!.AsObject();
            body.Remove(field).Should().BeTrue();
            (await SendAsync("/api/customer-mobile/checkout/submit", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest, field);
        }
        foreach (var field in new[] { "review", "contact", "paymentMethod", "expectedQuoteFingerprint" })
        {
            var body = JsonSerializer.SerializeToNode(submission, JsonOptions)!.AsObject();
            body["intent"]!.AsObject().Remove(field).Should().BeTrue();
            (await SendAsync("/api/customer-mobile/checkout/submit", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest, field);
        }
        foreach (var field in new[] { "total", "unitPrice", "isPaid", "paymentStatus", "gatewayTransactionId", "userId", "tableNumber" })
        {
            var body = JsonSerializer.SerializeToNode(submission, JsonOptions)!.AsObject();
            body["intent"]![field] = "untrusted";
            (await SendAsync("/api/customer-mobile/checkout/submit", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest, field);
        }
        var extraContact = JsonSerializer.SerializeToNode(submission, JsonOptions)!.AsObject();
        extraContact["intent"]!["contact"]!["userId"] = Guid.NewGuid();
        (await SendAsync("/api/customer-mobile/checkout/submit", extraContact)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync("/api/customer-mobile/checkout/submit", (JsonNode?)null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
        await OrderResolutionAsync(await SubmitAsync(submission)); // Valid control on the same actual route.
    }
}
