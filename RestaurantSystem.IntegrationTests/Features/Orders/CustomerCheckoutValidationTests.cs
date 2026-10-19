using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerCheckoutValidationTests(DatabaseFixture fixture) : CustomerCheckoutFixture(fixture)
{
    [Fact]
    public async Task Every_review_field_is_required_and_null_or_extra_fields_are_refused()
    {
        var basket = await CheckoutBasketAsync();
        var review = Review(basket);
        foreach (var field in new[] { "schemaVersion", "expectedBasketId", "expectedPurchaseFingerprint", "type", "pointsToRedeem", "tip" })
        {
            var body = JsonSerializer.SerializeToNode(review, JsonOptions)!.AsObject();
            body.Remove(field).Should().BeTrue();
            (await SendAsync(QuotePath, body)).StatusCode.Should().Be(HttpStatusCode.BadRequest, field);
        }
        foreach (var extra in new[] { "unitPrice", "total", "isPaid", "gatewayTransactionId", "tableNumber" })
        {
            var body = JsonSerializer.SerializeToNode(review, JsonOptions)!.AsObject();
            body[extra] = "untrusted";
            (await SendAsync(QuotePath, body)).StatusCode.Should().Be(HttpStatusCode.BadRequest, extra);
        }
        var missingDigest = JsonSerializer.SerializeToNode(review, JsonOptions)!.AsObject();
        missingDigest["expectedPurchaseFingerprint"] = null;
        (await SendAsync(QuotePath, missingDigest)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(QuotePath, (JsonNode?)null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await QuoteAsync(review)).Money.Total.Should().Be(17m); // Valid request exercises the same endpoint.
        await AssertNoFinancialEffectsAsync();
    }

    [Fact]
    public async Task Invalid_version_revision_type_points_and_money_are_refused_before_pricing()
    {
        var basket = await CheckoutBasketAsync();
        var valid = Review(basket);
        var invalid = new[]
        {
            valid with { SchemaVersion = 0 }, valid with { SchemaVersion = 2 },
            valid with { ExpectedBasketId = Guid.Empty },
            valid with { ExpectedPurchaseFingerprint = "unreviewed" },
            valid with { Type = OrderType.DineIn }, valid with { Type = (OrderType)99 },
            valid with { PointsToRedeem = -1 }, valid with { Tip = -0.01m },
            valid with { Tip = 0.001m }, valid with { Tip = 100_000_000m }
        };
        foreach (var review in invalid)
            (await QuoteResponseAsync(review)).StatusCode.Should().Be(HttpStatusCode.BadRequest, review.ToString());
        (await QuoteAsync(valid with { Tip = 0.000m })).Money.Total.Should().Be(14m);
        await AssertNoFinancialEffectsAsync();
    }
}
