using System.Net;
using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketChannelPayloadTests(DatabaseFixture fixture) : CustomerBasketChannelFixture(fixture)
{
    [Theory]
    [InlineData("operationId")]
    [InlineData("expectedBasketId")]
    [InlineData("expectedPurchaseFingerprint")]
    [InlineData("orderType")]
    [InlineData("confirmedConflictItemIds")]
    public async Task Required_fields_cannot_be_omitted_and_silently_defaulted(string field)
    {
        var body = JsonSerializer.SerializeToElement(Channel(), JsonOptions)
            .EnumerateObject().Where(property => property.Name != field)
            .ToDictionary(property => property.Name, property => property.Value);
        (await SendAsync("/api/customer-mobile/basket/channel", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReceiptsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_revisions_types_or_removal_sets_cannot_create_effects_or_receipts()
    {
        var request = Channel();
        foreach (var invalid in new[]
        {
            request with { OperationId = Guid.Empty },
            request with { OrderType = (OrderType)99 },
            request with { ExpectedPurchaseFingerprint = new string('A', 64) },
            request with { ExpectedBasketId = Guid.NewGuid() },
            request with { ConfirmedConflictItemIds = [Guid.Empty] },
            request with { ConfirmedConflictItemIds = [request.OperationId, request.OperationId] },
            request with { ConfirmedConflictItemIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() }
        })
            (await SwitchAsync(invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReceiptsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_refused_payload_can_still_be_durably_sealed_before_forgetting_it()
    {
        var request = Channel() with { ExpectedPurchaseFingerprint = "invalid" };
        (await SwitchAsync(request)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var proof = await ResolutionAsync(await CancelChannelAsync(request));
        proof.Outcome.Should().Be(Api.Features.Basket.Dtos.CustomerBasketOperationOutcome.Cancelled);
        (await ReceiptsAsync()).Should().ContainSingle().Which.IsCancelled.Should().BeTrue();
    }
}
