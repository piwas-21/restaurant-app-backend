using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

public abstract class ChannelDecisionTestBase(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    protected static string DecisionEndpoint(Guid orderId) => $"{Endpoint}/{orderId}/decision";
    protected const string ClaimEndpoint = "/api/delivery-channels/decisions/claim";
    protected static string ReportEndpoint(Guid decisionId) => $"/api/delivery-channels/decisions/{decisionId}/report";

    protected void Human(UserRole role)
    {
        Client.DefaultRequestHeaders.Authorization = null;
        AuthenticateAsRole(role);
    }

    protected async Task Gateway()
        => AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.ChannelOrdersWrite]));

    protected async Task<(Guid OrderId, ChannelDecisionRequest Request)> HeldOrder(string action = "accept")
    {
        var request = await PrepareAsync();
        var imported = await ImportAsync(request);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.SingleAsync(order => order.Id == imported.OrderId);
        Human(UserRole.Cashier);
        return (order.Id, new()
        {
            OperationId = Guid.NewGuid(),
            Action = action,
            Reason = "Authorized sandbox decision. No food or courier.",
            ExpectedVersion = order.Version
        });
    }

    protected async Task Queue(Guid orderId, ChannelDecisionRequest request)
    {
        var response = await PostAsJsonAsync(DecisionEndpoint(orderId), request);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
    }

    protected async Task<ChannelDecisionLeaseDto> Claim()
    {
        await Gateway();
        var response = await Client.PostAsync(ClaimEndpoint, null);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ChannelDecisionLeaseDto>(JsonOptions))!;
    }

    protected static ChannelDecisionReport Report(ChannelDecisionLeaseDto lease, string state = "Succeeded", string canonicalState = "ACCEPTED")
        => new()
        {
            LeaseId = lease.LeaseId,
            State = state,
            CanonicalState = canonicalState,
            CanonicalHash = canonicalState == "UNKNOWN" ? string.Empty : new string('b', 64),
            ObservedAt = PostgreSqlTimestamp()
        };
    private static DateTimeOffset PostgreSqlTimestamp()
    {
        // PostgreSQL timestamps retain microseconds; keep the exact persistence assertion strict.
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
