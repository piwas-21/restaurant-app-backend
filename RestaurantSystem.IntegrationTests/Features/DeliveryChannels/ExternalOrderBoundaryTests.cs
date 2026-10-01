using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ExternalOrderBoundaryTests(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    [Theory]
    [InlineData("anonymous")]
    [InlineData("human-admin")]
    [InlineData("orders:write")]
    [InlineData("channels:orders:write")]
    public async Task OnlyDedicatedMachineScopeCanImport(string identity)
    {
        var request = await PrepareAsync();
        AuthenticateAsAnonymous();
        switch (identity)
        {
            case "human-admin": AuthenticateAsAdmin(); break;
            case "orders:write": AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.OrdersWrite])); break;
            case "channels:orders:write": AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.ChannelOrdersWrite])); break;
        }
        var response = await PostAsJsonAsync(Endpoint, request);
        response.StatusCode.Should().Be(identity switch
        {
            "channels:orders:write" => HttpStatusCode.OK,
            "orders:write" => HttpStatusCode.Forbidden,
            _ => HttpStatusCode.Unauthorized,
        });
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(identity == "channels:orders:write" ? 1 : 0);
    }

    [Theory]
    [InlineData("store")]
    [InlineData("disabled")]
    [InlineData("production")]
    [InlineData("duplicate-binding")]
    [InlineData("currency")]
    public async Task DeploymentBindingControlsTenantStoreCurrencyAndEnvironment(string fault)
    {
        var request = await PrepareAsync();
        var settings = Factory.Services.GetRequiredService<IOptions<DeliveryChannelSettings>>().Value;
        switch (fault)
        {
            case "store": request = request with { StoreId = "another-tenant-store" }; break;
            case "disabled": settings.Enabled = false; break;
            case "production": settings.Stores[0].IsSandbox = false; break;
            case "duplicate-binding": settings.Stores.Add(settings.Stores[0]); break;
            case "currency": request = request with { Currency = "EUR" }; break;
        }
        var response = await PostAsJsonAsync(Endpoint, request);
        response.StatusCode.Should().Be(fault == "currency" ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("modifier")]
    [InlineData("user")]
    [InlineData("overflow")]
    [InlineData("rounding")]
    [InlineData("total")]
    [InlineData("cash")]
    [InlineData("printer-control")]
    [InlineData("long-note")]
    [InlineData("null-items")]
    [InlineData("null-item")]
    [InlineData("missing-merchant-total")]
    [InlineData("missing-timestamp")]
    [InlineData("missing-unit-price")]
    [InlineData("missing-item-total")]
    [InlineData("missing-all-money")]
    public async Task UnsupportedOrMalformedInput_IsRejectedBeforeAnyOrderWrite(string fault)
    {
        var request = await PrepareAsync();
        var json = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(request, JsonOptions))!.AsObject();
        var item = json["items"]![0]!.AsObject();
        switch (fault)
        {
            case "modifier": item["modifiers"] = new JsonArray(); break;
            case "user": json["userId"] = Guid.NewGuid().ToString(); break;
            case "overflow": item["unitPrice"] = decimal.MaxValue; break;
            case "rounding": item["unitPrice"] = 5.001m; break;
            case "total": json["merchantTotal"] = 6; break;
            case "cash": json["fulfillmentType"] = "DELIVERY_BY_RESTAURANT"; break;
            case "printer-control": item["instructions"] = "allergy\u001b@injection"; break;
            case "long-note": item["instructions"] = new string('a', 501); break;
            case "null-items": json["items"] = null; break;
            case "null-item": json["items"] = new JsonArray((JsonNode?)null); break;
            case "missing-merchant-total": json.Remove("merchantTotal"); break;
            case "missing-timestamp": json.Remove("placedAt"); break;
            case "missing-unit-price": item.Remove("unitPrice"); break;
            case "missing-item-total": item.Remove("total"); break;
            case "missing-all-money": json.Remove("merchantTotal"); item.Remove("unitPrice"); item.Remove("total"); break;
        }
        var response = await Client.PostAsync(Endpoint, new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.ExternalOrderReferences.CountAsync()).Should().Be(0);
    }
}
