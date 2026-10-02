using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelDecisionTimingTests(DatabaseFixture fixture) : ChannelDecisionTestBase(fixture)
{
    [Fact]
    public async Task DeliveryUsesConfiguredLeaseRetryAndObservationBounds()
    {
        var (orderId, request) = await HeldOrder(); await Queue(orderId, request);
        using var scope = Factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<DeliveryChannelSettings>>().Value;
        var original = (settings.DecisionLeaseSeconds, settings.DecisionRetrySeconds, settings.DecisionClockToleranceSeconds);
        settings.DecisionLeaseSeconds = 180; settings.DecisionRetrySeconds = 90; settings.DecisionClockToleranceSeconds = 2;
        try
        {
            var before = DateTime.UtcNow;
            var lease = await Claim();
            // A hardcoded two-minute lease fails this independent three-minute oracle.
            lease.LeaseUntil.Should().BeOnOrAfter(before.AddSeconds(175)).And.BeOnOrBefore(DateTime.UtcNow.AddSeconds(180));
            (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease) with { ObservedAt = DateTimeOffset.UtcNow.AddSeconds(10) }))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var retryBefore = DateTime.UtcNow;
            (await PostAsJsonAsync(ReportEndpoint(lease.DecisionId), Report(lease, "Unknown", "UNKNOWN"))).StatusCode.Should().Be(HttpStatusCode.OK);
            await using var context = DatabaseFixture.CreateContext();
            var job = await context.ChannelOrderDecisions.SingleAsync(job => job.Id == lease.DecisionId);
            job.AvailableAt.Should().BeOnOrAfter(retryBefore.AddSeconds(85)).And.BeOnOrBefore(DateTime.UtcNow.AddSeconds(90));
        }
        finally
        {
            (settings.DecisionLeaseSeconds, settings.DecisionRetrySeconds, settings.DecisionClockToleranceSeconds) = original;
        }
    }
}
