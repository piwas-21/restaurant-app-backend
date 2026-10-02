using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderAmendmentRequestPresenceTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("reviewAcknowledged")]
    [InlineData("preparingOverrideAcknowledged")]
    [InlineData("releaseAdditionsToKitchen")]
    [InlineData("localProviderSupplementConsent")]
    public void Quote_RequiresExplicitAcknowledgmentAndDispatchChoices(string property)
    {
        var request = new OrderAmendmentQuoteRequest { ExpectedOrderVersion = 1 };
        var json = JsonSerializer.SerializeToNode(request, Options)!.AsObject();
        JsonSerializer.Deserialize<OrderAmendmentQuoteRequest>(json, Options)
            .Should().NotBeNull("explicit false is a valid choice");
        json.Remove(property).Should().BeTrue();
        var read = () => JsonSerializer.Deserialize<OrderAmendmentQuoteRequest>(json, Options);
        read.Should().Throw<JsonException>("omitted input must not silently become false");
    }

    [Fact]
    public void Commit_RequiresExplicitReviewAcknowledgment()
    {
        var json = JsonSerializer.SerializeToNode(new OrderAmendmentCommitRequest
        {
            AmendmentId = Guid.NewGuid(),
            ClientOperationId = Guid.NewGuid(),
            ExpectedOrderVersion = 1
        }, Options)!.AsObject();
        JsonSerializer.Deserialize<OrderAmendmentCommitRequest>(json, Options)!.ReviewAcknowledged.Should().BeFalse();
        json.Remove("reviewAcknowledged").Should().BeTrue();
        var read = () => JsonSerializer.Deserialize<OrderAmendmentCommitRequest>(json, Options);
        read.Should().Throw<JsonException>();
    }

    [Fact]
    public void GuestRound_RequiresOperationIdentityInThePayload()
    {
        var json = JsonSerializer.SerializeToNode(new CreateGuestRoundRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 1,
            ExpectedBasketFingerprint = "fingerprint"
        }, Options)!.AsObject();
        JsonSerializer.Deserialize<CreateGuestRoundRequest>(json, Options)!.OperationId.Should().NotBeEmpty();
        json.Remove("operationId").Should().BeTrue();
        var read = () => JsonSerializer.Deserialize<CreateGuestRoundRequest>(json, Options);
        read.Should().Throw<JsonException>();
    }
}
