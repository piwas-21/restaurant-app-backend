using FluentAssertions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// Pins the role gate on the service SSE stream.
///
/// GET /api/events/service was [Authorize]-only while every event it pushes carries a full
/// OrderDto — customer name, email, phone, delivery address and payment rows — and the replay
/// service hands a new subscriber the recent buffer on connect. That is the same PII leak #256
/// and #258 closed on the id-addressed order routes, except a customer needed no order id at
/// all, only a connection, and got a live feed rather than one order. Its sibling streams were
/// already gated (kitchen, all); this one was the outlier and was missed by both.
/// </summary>
[Collection("Database Lane 4")]
public class OrderEventStreamAuthorizationTests : IntegrationTestBase
{
    public OrderEventStreamAuthorizationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const string ServiceStreamRoute = "/api/events/service";

    [Fact]
    public async Task Customer_SubscribingToTheServiceStream_IsForbidden()
    {
        AuthenticateAsUser();

        var response = await OpenStream(ServiceStreamRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the stream pushes every order's PII, so it is back-of-house only");
    }

    [Fact]
    public async Task Anonymous_SubscribingToTheServiceStream_IsChallenged()
    {
        AuthenticateAsAnonymous();

        var response = await OpenStream(ServiceStreamRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The cashier till and the server floor view are the two real subscribers, so the gate has
    /// to admit the non-admin staff roles — [RequireAdmin] here would close the leak and take
    /// both surfaces down with it.
    /// </summary>
    [Theory]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.KitchenStaff)]
    public async Task EveryStaffRole_CanSubscribeToTheServiceStream(UserRole role)
    {
        AuthenticateAsRole(role);

        var response = await OpenStream(ServiceStreamRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.MediaType.Should().Be("text/event-stream");
    }

    /// <summary>What a caller learns from the stream without ever reading an event off it.</summary>
    private sealed record StreamHandshake(HttpStatusCode StatusCode, string? MediaType);

    /// <summary>
    /// Reads response headers only, then disposes without draining the body — an SSE response
    /// never completes, so the default ResponseContentRead would block until the test host tore
    /// the connection down. The two values are captured BEFORE disposing, since reading them off
    /// a disposed response throws.
    /// </summary>
    private async Task<StreamHandshake> OpenStream(string route)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        return new StreamHandshake(response.StatusCode, response.Content.Headers.ContentType?.MediaType);
    }
}
