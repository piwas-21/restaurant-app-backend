using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed class CustomerOrderCancellationValidationTests(DatabaseFixture fixture) : CustomerOrderCancellationFixture(fixture)
{
    [Fact]
    public async Task Strict_http_shapes_refuse_missing_schema_unknown_fields_and_unbounded_versions_before_any_write()
    {
        var request = await AwaitingDelayAsync();
        foreach (var defect in new[] { "missing-proof", "null-intent", "private-field", "schema", "zero-version", "overflow-version", "uppercase-hash" })
        {
            var body = JsonSerializer.SerializeToNode(request, JsonOptions)!.AsObject();
            var intent = body["intent"]!.AsObject();
            switch (defect)
            {
                case "missing-proof": body.Remove("proof"); break;
                case "null-intent": body["intent"] = null; break;
                case "private-field": intent["refundAmount"] = 100; break;
                case "schema": intent["schemaVersion"] = 2; break;
                case "zero-version": intent["expectedVersion"] = 0; break;
                case "overflow-version": intent["expectedVersion"] = int.MaxValue; break;
                case "uppercase-hash": intent["payloadHash"] = request.Intent.PayloadHash.ToUpperInvariant(); break;
                default: throw new ArgumentException("Unknown validation defect", nameof(defect));
            }
            foreach (var phase in new[] { "submit", "recover" })
                (await SendAsync(CancellationPath + "/original/" + phase, body)).StatusCode.Should().Be(HttpStatusCode.BadRequest, defect);
        }
        foreach (var session in new[] { "", "invalid", Guid.Empty.ToString("D"), "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA" })
            (await SendAsync(CancellationPath + "/original/submit", request, session)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await DecisionsAsync()).Should().BeEmpty();
        (await CancelAsync(request)).Outcome.Should().Be(CustomerOrderCancellationOutcome.Cancelled);
    }
}
