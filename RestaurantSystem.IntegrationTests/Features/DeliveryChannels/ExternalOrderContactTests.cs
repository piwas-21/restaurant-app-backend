using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ExternalOrderContactTests(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    [Fact]
    public async Task ImportPersistsCodeSeparatelyAndReplayCannotChangeIt()
    {
        var request = await PrepareAsync();
        request = request with { CustomerPhone = "+31 200000000", CustomerPhoneAccessCode = "555 55 555" };
        var imported = await ImportAsync(request);
        (await ImportAsync(request)).AlreadyImported.Should().BeTrue();
        var conflict = await PostAsJsonAsync(Endpoint, request with { CustomerPhoneAccessCode = "12345" });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var db = DatabaseFixture.CreateContext();
        var order = await db.Orders.SingleAsync(order => order.Id == imported.OrderId);
        order.CustomerPhone.Should().Be("+31 200000000");
        order.ExternalReference!.CustomerPhoneAccessCode.Should().Be("555 55 555");
        order.Notes.Should().Be(request.Instructions);
        Client.DefaultRequestHeaders.Authorization = null;
        AuthenticateAsAdmin();
        var reply = await Client.GetAsync($"/api/Orders/{imported.OrderId}");
        reply.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("data").GetProperty("externalOrder").GetProperty("customerPhoneAccessCode")
            .GetString().Should().Be("555 55 555");
    }

    [Theory]
    [InlineData(null, "12345")]
    [InlineData("+31 200000000", " ")]
    [InlineData("+31 200000000", "bad")]
    [InlineData("+31 200000000", "123\u001b45")]
    [InlineData("+31 200000000", "1234567890123456789012345678901")]
    public async Task MalformedContactCodesDoNotPersistOrders(string? phone, string code)
    {
        var request = await PrepareAsync();
        var response = await PostAsJsonAsync(Endpoint, request with { CustomerPhone = phone, CustomerPhoneAccessCode = code });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var db = DatabaseFixture.CreateContext();
        (await db.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AbsentCodeKeepsLegacyFingerprintShape()
    {
        var request = await PrepareAsync();
        JsonSerializer.Serialize(request).Should().NotContain("CustomerPhoneAccessCode");
        (await ImportAsync(request)).AlreadyImported.Should().BeFalse();
        (await ImportAsync(request)).AlreadyImported.Should().BeTrue();
    }
}
