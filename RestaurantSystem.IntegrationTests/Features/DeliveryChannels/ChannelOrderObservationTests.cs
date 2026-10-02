using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelOrderObservationTests(DatabaseFixture fixture) : ChannelDecisionTestBase(fixture)
{
    private static string ObserveEndpoint(Guid id) => $"{Endpoint}/{id}/observe";
    private static ChannelOrderObservation Observation(string state, DateTimeOffset? time = null) => new()
    {
        Provider = "uber-eats",
        StoreId = "approved-test-store",
        ExternalOrderId = "provider-order-1",
        CanonicalState = state,
        CanonicalHash = new string('c', 64),
        ObservedAt = time ?? DateTimeOffset.UtcNow,
    };

    [Theory]
    [InlineData("CANCELED", OrderStatus.Cancelled)]
    [InlineData("DENIED", OrderStatus.Cancelled)]
    [InlineData("FINISHED", OrderStatus.Completed)]
    public async Task LaterTerminalEvidenceClosesKitchenPreservesMoneyAndHistoricalDecision(string state, OrderStatus expected)
    {
        var (id, request) = await HeldOrder(); await Queue(id, request); var lease = await Claim();
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease))).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var before = DatabaseFixture.CreateContext();
        var original = await before.Orders.SingleAsync(order => order.Id == id);
        var fingerprint = original.ExternalReference!.PayloadHash; var releaseAt = original.KitchenReleasedAt;
        var observation = Observation(state);
        var response = await PostAsJsonAsync(ObserveEndpoint(id), observation);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var reply = await response.Content.ReadFromJsonAsync<ChannelOrderObservationDto>(JsonOptions);
        reply!.IsTerminal.Should().BeTrue(); reply.CanonicalState.Should().Be(state);
        (await PostAsJsonAsync(ObserveEndpoint(id), observation)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var after = DatabaseFixture.CreateContext();
        var order = await after.Orders.Include(order => order.Payments).SingleAsync(order => order.Id == id);
        order.Status.Should().Be(expected); order.IsKitchenReleased.Should().BeFalse(); order.KitchenReleasedAt.Should().Be(releaseAt);
        order.Total.Should().Be(5); order.TotalPaid.Should().Be(5); order.RemainingAmount.Should().Be(0);
        order.Payments.Should().ContainSingle().Which.Status.Should().Be(PaymentStatus.Completed);
        order.ExternalReference!.PayloadHash.Should().Be(fingerprint); order.ExternalReference.ReportedTax.Should().BeNull();
        order.ExternalReference.CanonicalHash.Should().Be(new string('c', 64));
        (await after.ChannelOrderDecisions.SingleAsync()).State.Should().Be("Succeeded");
        (await after.OrderStatusHistories.CountAsync(history => history.OrderId == id)).Should().Be(2);
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation("ACCEPTED"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        Human(UserRole.Cashier);
        var dto = (await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{id}"))!.Data!;
        dto.PermittedActions!.Single(action => action.Action == "PrintKitchen").Allowed.Should().BeFalse();
        dto.PermittedActions!.Single(action => action.Action == "CollectPayment").Allowed.Should().BeFalse();
    }

    [Theory]
    [InlineData("accept", "CANCELED", "Failed")]
    [InlineData("accept", "DENIED", "Failed")]
    [InlineData("deny", "DENIED", "Succeeded")]
    [InlineData("accept", "FINISHED", "Succeeded")]
    public async Task TerminalObservationInvalidatesInFlightLeaseAndNeverReleasesHeldOrder(string action, string state, string decisionState)
    {
        var (id, request) = await HeldOrder(action); await Queue(id, request); var lease = await Claim();
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation(state))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(order => order.Id == id)).IsKitchenReleased.Should().BeFalse();
        var job = await context.ChannelOrderDecisions.SingleAsync();
        job.State.Should().Be(decisionState); job.LeaseId.Should().BeNull(); job.LeaseUntil.Should().BeNull();
    }

    [Fact]
    public async Task CanonicalAcceptanceWithoutHumanDecisionCannotReleaseKitchen()
    {
        var (id, _) = await HeldOrder(); await Gateway();
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation("ACCEPTED"))).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.SingleAsync(order => order.Id == id);
        order.Status.Should().Be(OrderStatus.PendingApproval); order.IsKitchenReleased.Should().BeFalse();
    }

    [Theory]
    [InlineData("Provider")]
    [InlineData("Store")]
    [InlineData("Order")]
    public async Task ObservationMustMatchAllIdentityFields(string changed)
    {
        var (id, _) = await HeldOrder(); await Gateway(); var request = Observation("CANCELED");
        request = changed switch { "Provider" => request with { Provider = "other" }, "Store" => request with { StoreId = "other" }, _ => request with { ExternalOrderId = "other" } };
        (await PostAsJsonAsync(ObserveEndpoint(id), request)).StatusCode.Should().Be(changed == "Provider" ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(order => order.Id == id)).Status.Should().Be(OrderStatus.PendingApproval);
    }

    [Fact]
    public async Task ObservationRequiresScopedMachineAndRejectsUnknownStaleFutureConflictingOrReversedEvidence()
    {
        var (id, _) = await HeldOrder();
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation("CANCELED"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.OrdersWrite]));
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation("CANCELED"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await Gateway();
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation("UNKNOWN"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation("CANCELED", DateTimeOffset.UtcNow.AddDays(1)))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation("CANCELED", DateTimeOffset.MinValue))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var now = DateTimeOffset.UtcNow; now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation("ACCEPTED", now))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation("CANCELED", now) with { CanonicalHash = new string('d', 64) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation("CREATED"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PostAsJsonAsync(ObserveEndpoint(id), Observation("CANCELED", now.AddSeconds(-1)))).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
