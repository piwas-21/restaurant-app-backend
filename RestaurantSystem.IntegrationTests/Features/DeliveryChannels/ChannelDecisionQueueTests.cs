using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelDecisionQueueTests(DatabaseFixture fixture) : ChannelDecisionTestBase(fixture)
{
    [Theory]
    [InlineData(UserRole.Admin, HttpStatusCode.Accepted)]
    [InlineData(UserRole.Cashier, HttpStatusCode.Accepted)]
    [InlineData(UserRole.Server, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.KitchenStaff, HttpStatusCode.Forbidden)]
    [InlineData(UserRole.Customer, HttpStatusCode.Forbidden)]
    public async Task OnlyHumanCashierOrAdminCanOriginateDecision(UserRole role, HttpStatusCode expected)
    {
        var (orderId, request) = await HeldOrder(); Human(role);
        var response = await PostAsJsonAsync(DecisionEndpoint(orderId), request);
        response.StatusCode.Should().Be(expected);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.SingleAsync(order => order.Id == orderId);
        order.Status.Should().Be(OrderStatus.PendingApproval);
        order.IsKitchenReleased.Should().BeFalse();
    }

    [Fact]
    public async Task ScopedGatewayCannotOriginateStaffDecision()
    {
        var (orderId, request) = await HeldOrder(); await Gateway();
        (await PostAsJsonAsync(DecisionEndpoint(orderId), request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MatchingConcurrentReplayCreatesOneDurableDecision()
    {
        var (orderId, request) = await HeldOrder();
        var responses = await Task.WhenAll(PostAsJsonAsync(DecisionEndpoint(orderId), request), PostAsJsonAsync(DecisionEndpoint(orderId), request));
        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.Accepted);
        await using var context = DatabaseFixture.CreateContext();
        var job = await context.ChannelOrderDecisions.SingleAsync(job => job.OrderId == orderId);
        job.OperationId.Should().Be(request.OperationId);
        job.CreatedBy.Should().NotBe("System");
        job.State.Should().Be("Pending");
        job.Attempts.Should().Be(0);
        (await context.Orders.SingleAsync(order => order.Id == orderId)).IsKitchenReleased.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptDenyRaceCannotCreateConflictingJobs()
    {
        var (orderId, request) = await HeldOrder();
        var responses = await Task.WhenAll(PostAsJsonAsync(DecisionEndpoint(orderId), request),
            PostAsJsonAsync(DecisionEndpoint(orderId), request with { Action = "deny", OperationId = Guid.NewGuid() }));
        responses.Select(response => response.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Accepted, HttpStatusCode.Conflict]);
        await using var context = DatabaseFixture.CreateContext();
        (await context.ChannelOrderDecisions.CountAsync(job => job.OrderId == orderId)).Should().Be(1);
    }

    [Fact]
    public async Task ChangedReplayOrStaleVersionIsRefused()
    {
        var (orderId, request) = await HeldOrder();
        (await PostAsJsonAsync(DecisionEndpoint(orderId), request with { ExpectedVersion = request.ExpectedVersion + 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        await Queue(orderId, request);
        (await PostAsJsonAsync(DecisionEndpoint(orderId), request with { Reason = "Changed instruction" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("accept", "")]
    [InlineData("accept", "\u001b@")]
    [InlineData("ready", "A reason")]
    public async Task InvalidDecisionIsRefused(string action, string reason)
    {
        var (orderId, request) = await HeldOrder();
        (await PostAsJsonAsync(DecisionEndpoint(orderId), request with { Action = action, Reason = reason }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MissingVersionIsUnderposting_StaffReadLeaksNoMachineEnvelope()
    {
        var (orderId, request) = await HeldOrder();
        (await PostAsJsonAsync(DecisionEndpoint(orderId), new { request.OperationId, request.Action, request.Reason }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await Queue(orderId, request);
        var response = await Client.GetAsync(DecisionEndpoint(orderId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Pending").And.NotContain("leaseId").And.NotContain("externalOrderId").And.NotContain("storeId");
    }
}
