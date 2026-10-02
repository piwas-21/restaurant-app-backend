using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelDecisionDeliveryTests(DatabaseFixture fixture) : ChannelDecisionTestBase(fixture)
{
    [Theory]
    [InlineData("accept", "ACCEPTED", OrderStatus.Confirmed, true)]
    [InlineData("accept", "FINISHED", OrderStatus.Completed, false)]
    [InlineData("deny", "DENIED", OrderStatus.Cancelled, false)]
    public async Task OnlyMatchingProviderEvidenceChangesLocalLifecycle(string action, string canonical, OrderStatus status, bool released)
    {
        var (orderId, request) = await HeldOrder(action); await Queue(orderId, request);
        var lease = await Claim();
        lease.OrderId.Should().Be(orderId);
        var report = Report(lease, canonicalState: canonical);
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), report)).StatusCode.Should().Be(HttpStatusCode.OK);
        // Exact report replay after a lost HTTP response must not change the order again.
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), report)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.Include(order => order.Payments).SingleAsync(order => order.Id == orderId);
        order.Status.Should().Be(status); order.IsKitchenReleased.Should().Be(released);
        if (released)
        {
            order.KitchenReleasedAt.Should().Be(report.ObservedAt.UtcDateTime);
            order.KitchenReleasedBy.Should().NotBeNullOrWhiteSpace().And.NotBe("System");
            (await context.OrderRoutingStates.CountAsync(route => route.OrderId == orderId)).Should().BeGreaterThan(0);
        }
        else
        {
            order.KitchenReleasedAt.Should().BeNull();
            (await context.OrderRoutingStates.CountAsync(route => route.OrderId == orderId)).Should().Be(0);
        }
        order.Total.Should().Be(5); order.TotalPaid.Should().Be(5); order.RemainingAmount.Should().Be(0);
        order.ExternalReference!.ExternalState.Should().Be(canonical);
        order.ExternalReference.ReportedTax.Should().BeNull();
        order.ExternalReference.PayloadHash.Should().NotBe(new string('b', 64));
        order.Payments.Should().ContainSingle().Which.PaymentGateway.Should().Be("uber-eats");
        (await context.OrderStatusHistories.CountAsync(history => history.OrderId == orderId)).Should().Be(1);
        Human(UserRole.Cashier);
        var response = await GetFromJsonAsync<ApiResponse<OrderDto>>($"/api/Orders/{orderId}");
        response!.Success.Should().BeTrue();
        response.Data.Should().NotBeNull();
        var dto = response.Data!;
        dto.Id.Should().Be(orderId);
        dto.PermittedActions.Should().NotBeNull();
        var actions = dto.PermittedActions!;
        actions.Single(action => action.Action == "PrintReceipt").Allowed.Should().Be(canonical is "ACCEPTED" or "FINISHED");
        actions.Single(action => action.Action == "PrintKitchen").Allowed.Should().Be(released);
    }

    [Fact]
    public async Task UnknownDeliveryRemainsHeldAndRetryable()
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request);
        var lease = await Claim();
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease, "Unknown", "UNKNOWN")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = DatabaseFixture.CreateContext();
        var job = await context.ChannelOrderDecisions.SingleAsync(job => job.Id == lease.DecisionId);
        job.State.Should().Be("Unknown"); job.LastCanonicalHash.Should().BeNull();
        var order = await context.Orders.SingleAsync(order => order.Id == orderId);
        order.Status.Should().Be(OrderStatus.PendingApproval); order.IsKitchenReleased.Should().BeFalse();
        job.AvailableAt = DateTime.UtcNow.AddSeconds(-1); await context.SaveChangesAsync();
        var retry = await Claim(); retry.Attempt.Should().Be(2); retry.LeaseId.Should().NotBe(lease.LeaseId);
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PostAsJsonAsync(ReportEndpoint(retry.DecisionId), Report(retry)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Succeeded", "CREATED")]
    [InlineData("Succeeded", "DENIED")]
    [InlineData("Unknown", "ACCEPTED")]
    [InlineData("Unknown", "CANCELED")]
    public async Task ConflictingOrUnconfirmedResultCannotReleaseKitchen(string state, string canonical)
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request); var lease = await Claim();
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease, state, canonical)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(order => order.Id == orderId)).IsKitchenReleased.Should().BeFalse();
    }

    [Fact]
    public async Task CancelBeforeConfirmationNeverStartsPreparation()
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request); var lease = await Claim();
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease, "Failed", "CANCELED")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.SingleAsync(order => order.Id == orderId);
        order.Status.Should().Be(OrderStatus.Cancelled); order.IsKitchenReleased.Should().BeFalse();
    }

    [Fact]
    public async Task OnlyScopedMachineCanClaim_ConcurrentClaimsDoNotDuplicateDelivery()
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request);
        (await Client.PostAsync(ClaimEndpoint, null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await Gateway();
        var results = await Task.WhenAll(Client.PostAsync(ClaimEndpoint, null), Client.PostAsync(ClaimEndpoint, null));
        results.Select(response => response.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.NoContent]);
    }
}
