using System.Net;
using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

[Collection("Database Lane 3")]
public sealed class CustomerTableRoundContractTests(DatabaseFixture fixture) : CustomerTableRoundFixture(fixture)
{
    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("operationId")]
    [InlineData("tenantId")]
    [InlineData("serviceSessionId")]
    [InlineData("expectedAccountRevision")]
    [InlineData("expectedBasketId")]
    [InlineData("expectedPurchaseFingerprint")]
    public async Task Preparation_requires_every_explicit_reviewed_field(string missing)
    {
        var intent = await RoundIntentAsync();
        var body = JsonSerializer.SerializeToNode(intent, JsonOptions)!.AsObject();
        body.Remove(missing).Should().BeTrue();
        (await SendAsync(RoundPath + "prepare", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
    }
    [Theory]
    [InlineData("customerId")]
    [InlineData("tableNumber")]
    [InlineData("items")]
    [InlineData("payments")]
    [InlineData("tip")]
    [InlineData("pointsToRedeem")]
    [InlineData("total")]
    public async Task Customer_inputs_cannot_inject_identity_items_tenders_or_staff_authority(string field)
    {
        var intent = await RoundIntentAsync();
        var body = JsonSerializer.SerializeToNode(intent, JsonOptions)!.AsObject();
        body[field] = "untrusted";
        (await SendAsync(RoundPath + "prepare", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoFinancialEffectsAsync();
    }
    [Theory]
    [InlineData("visit")]
    [InlineData("revision")]
    [InlineData("basket")]
    [InlineData("fingerprint")]
    public async Task Submission_refuses_changed_prepared_input_before_an_effect(string field)
    {
        var intent = await RoundIntentAsync(); var prepared = await PrepareRoundAsync(intent);
        var submitted = Submission(intent, prepared);
        var changed = field switch
        {
            "visit" => submitted with { ServiceSessionId = Guid.NewGuid() },
            "revision" => submitted with { ExpectedAccountRevision = submitted.ExpectedAccountRevision + 1 },
            "basket" => submitted with { ExpectedBasketId = Guid.NewGuid() },
            _ => submitted with { ExpectedPurchaseFingerprint = new string('B', 64) }
        };
        (await SendAsync(RoundPath + "submit", changed)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await RoundResolutionAsync(await ResolveRoundAsync(prepared))).Outcome.Should().Be(CustomerOrderOperationOutcome.Unseen);
        await AssertNoFinancialEffectsAsync();
    }
}
