using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelDecisionBoundaryTests(DatabaseFixture fixture) : ChannelDecisionTestBase(fixture)
{
    [Fact]
    public async Task ExpiredLeaseCannotApplyResult_ReplacementLeaseCan()
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request); var original = await Claim();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var job = await context.ChannelOrderDecisions.SingleAsync(job => job.Id == original.DecisionId);
            job.LeaseUntil = DateTime.UtcNow.AddSeconds(-1); await context.SaveChangesAsync();
        }
        (await PostAsJsonAsync(ReportEndpoint(original.DecisionId), Report(original))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var replacement = await Claim(); replacement.LeaseId.Should().NotBe(original.LeaseId);
        (await PostAsJsonAsync(ReportEndpoint(replacement.DecisionId), Report(replacement))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangedConfirmedReportCannotOverwriteFrozenEvidence()
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request); var lease = await Claim();
        var report = Report(lease);
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), report)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), report with { CanonicalHash = new string('c', 64) }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var context = DatabaseFixture.CreateContext();
        (await context.ChannelOrderDecisions.SingleAsync(job => job.Id == lease.DecisionId)).LastCanonicalHash.Should().Be(new string('b', 64));
    }

    [Fact]
    public async Task MissingObservationOrWrongLeaseCannotReleaseOrder()
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request); var lease = await Claim();
        var report = Report(lease);
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), new { report.LeaseId, report.State, report.CanonicalState, report.CanonicalHash }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), report with { LeaseId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(order => order.Id == orderId)).IsKitchenReleased.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1440)]
    [InlineData(1440)]
    public async Task StaleOrFutureObservationIsRefused(int offsetMinutes)
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request); var lease = await Claim();
        await using var context = DatabaseFixture.CreateContext();
        var source = await context.ExternalOrderReferences.SingleAsync(source => source.OrderId == orderId);
        var observedAt = offsetMinutes < 0
            ? new DateTimeOffset(source.LastEventAt).AddMinutes(-1)
            : DateTimeOffset.UtcNow.AddMinutes(offsetMinutes);
        (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease) with { ObservedAt = observedAt }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OldOrderWriteScopeCannotDeliverDecisions()
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request);
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.OrdersWrite]));
        (await Client.PostAsync(ClaimEndpoint, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var lease = await Claim();
        lease.ExternalOrderId.Should().Be("provider-order-1");
        lease.StoreId.Should().Be("approved-test-store");
    }

    [Fact]
    public async Task RemovedOrderRetainsDecisionAnchorAndCannotBeClaimed()
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var order = await context.Orders.SingleAsync(order => order.Id == orderId);
            context.Orders.Remove(order); await context.SaveChangesAsync();
            order.IsDeleted.Should().BeTrue();
            (await context.ChannelOrderDecisions.CountAsync(job => job.OrderId == orderId)).Should().Be(1);
        }
        await Gateway();
        (await Client.PostAsync(ClaimEndpoint, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Human(UserRole.Cashier);
        (await Client.GetAsync(DecisionEndpoint(orderId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PausedChannelBlocksQueueAndDelivery_ButStaffCanReadDurableDecision()
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request);
        using var scope = Factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<DeliveryChannelSettings>>().Value;
        settings.Enabled = false;
        try
        {
            (await Client.GetAsync(DecisionEndpoint(orderId))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PostAsJsonAsync(DecisionEndpoint(orderId), request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await Gateway();
            (await Client.PostAsync(ClaimEndpoint, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally { settings.Enabled = true; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PausedOrAmbiguousStoreJobCannotStarveEnabledStore_AndCanResumeLater(bool duplicateBinding)
    {
        var (pausedOrderId, pausedRequest) = await HeldOrder(); await Queue(pausedOrderId, pausedRequest);
        using var scope = Factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<DeliveryChannelSettings>>().Value;
        var original = settings.Stores.ToList();
        var second = new DeliveryChannelStore { Provider = "uber-eats", StoreId = "second-test-store", Currency = "CHF", IsSandbox = true };
        settings.Stores.Add(second);
        try
        {
            var incoming = await PrepareAsync();
            var imported = await ImportAsync(incoming with { StoreId = second.StoreId, ExternalOrderId = "provider-order-2" });
            await using (var context = DatabaseFixture.CreateContext())
            {
                var order = await context.Orders.SingleAsync(order => order.Id == imported.OrderId);
                Human(UserRole.Cashier);
                await Queue(order.Id, pausedRequest with { OperationId = Guid.NewGuid(), ExpectedVersion = order.Version });
            }
            settings.Stores = duplicateBinding ? [original[0], original[0], second] : [second];
            var enabled = await Claim();
            enabled.StoreId.Should().Be(second.StoreId); enabled.ExternalOrderId.Should().Be("provider-order-2");
            await using (var context = DatabaseFixture.CreateContext())
                (await context.ChannelOrderDecisions.SingleAsync(job => job.OrderId == pausedOrderId)).State.Should().Be("Pending");
            settings.Stores = original;
            var resumed = await Claim();
            resumed.StoreId.Should().Be("approved-test-store"); resumed.ExternalOrderId.Should().Be("provider-order-1");
        }
        finally { settings.Stores = original; }
    }
}
