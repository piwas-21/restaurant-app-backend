using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ExternalOrderMutationTests(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    [Theory]
    [InlineData("status")]
    [InlineData("cancel")]
    [InlineData("delete")]
    [InlineData("approve")]
    [InlineData("release")]
    [InlineData("refund")]
    [InlineData("payment")]
    public async Task LocalMutation_CannotBypassProviderDecisionOrSettlement(string operation)
    {
        var imported = await ImportAsync(await PrepareAsync());
        Guid paymentId;
        int version;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var order = await context.Orders.Include(order => order.Payments).SingleAsync();
            paymentId = order.Payments.Single().Id;
            version = order.Version;
        }
        Client.DefaultRequestHeaders.Authorization = null;
        AuthenticateAsAdmin();
        var route = $"/api/orders/{imported.OrderId}";
        var response = operation switch
        {
            "status" => await PutAsJsonAsync(route + "/status", new { NewStatus = "Confirmed" }),
            "cancel" => await PostAsJsonAsync(route + "/cancel", new { CancellationReason = "local refusal" }),
            "delete" => await Client.DeleteAsync(route),
            "approve" => await PostAsJsonAsync(route + "/approve", new { PreparationMinutes = 10 }),
            "release" => await PostAsJsonAsync($"/api/staff/orders/{imported.OrderId}/release",
                new { ClientOperationId = Guid.NewGuid(), ExpectedVersion = version }),
            "refund" => await PostAsJsonAsync(route + $"/payments/{paymentId}/refund",
                new { RefundAmount = 5, RefundReason = "local refund" }),
            "payment" => await PostAsJsonAsync(route + "/payments",
                new { Amount = 5, PaymentMethod = "Cash", OperationId = Guid.NewGuid() }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var readback = DatabaseFixture.CreateContext();
        var persisted = await readback.Orders.Include(order => order.Payments).SingleAsync();
        persisted.Version.Should().Be(version);
        persisted.Status.Should().Be(OrderStatus.PendingApproval);
        persisted.IsKitchenReleased.Should().BeFalse();
        persisted.Payments.Should().ContainSingle().Which.RefundedAmount.Should().BeNull();
        persisted.Payments.Single().Status.Should().Be(PaymentStatus.Completed);
        persisted.Total.Should().Be(5);
        (await readback.OrderStatusHistories.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task StaffActions_DoNotOfferLocalDecisionSettlementOrKitchenPrinting()
    {
        var imported = await ImportAsync(await PrepareAsync());
        Client.DefaultRequestHeaders.Authorization = null;
        AuthenticateAsAdmin();
        var response = await Client.GetAsync($"/api/orders/{imported.OrderId}");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        var actions = json.RootElement.GetProperty("data").GetProperty("permittedActions").EnumerateArray().ToArray();
        foreach (var name in new[] { "Accept", "CancelOrder", "CollectPayment", "RefundPayment", "StartPreparing", "MarkReady", "PrintKitchen", "PrintReceipt" })
            actions.Single(action => action.GetProperty("action").GetString() == name).GetProperty("allowed").GetBoolean().Should().BeFalse(name);
        actions.Single(action => action.GetProperty("action").GetString() == "AddOperationalNote")
            .GetProperty("allowed").GetBoolean().Should().BeTrue();
    }
}
