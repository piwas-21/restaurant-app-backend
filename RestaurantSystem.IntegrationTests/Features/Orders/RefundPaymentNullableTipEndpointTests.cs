using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class RefundPaymentNullableTipEndpointTests(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private Guid _orderId;
    private Guid _paymentId;

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        _orderId = Guid.NewGuid();
        _paymentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var context = DatabaseFixture.CreateContext();
        var order = new Order
        {
            Id = _orderId,
            OrderNumber = $"REFUND-TIP-{_orderId:N}"[..20],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = 10m,
            Total = 10m,
            TotalPaid = 10m,
            RemainingAmount = 0m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(RefundPaymentNullableTipEndpointTests),
            Payments =
            {
                new OrderPayment
                {
                    Id = _paymentId,
                    PaymentMethod = PaymentMethod.Cash,
                    Amount = 10m,
                    TipMinor = 350,
                    Status = PaymentStatus.Completed,
                    Currency = "CHF",
                    PaymentDate = now,
                    CreatedAt = now,
                    CreatedBy = nameof(RefundPaymentNullableTipEndpointTests)
                }
            }
        };

        context.Orders.Add(order);
        await context.SaveChangesAsync();
    }

    [Theory]
    [InlineData("omitted", 1000, 0)]
    [InlineData("null", 1000, 0)]
    [InlineData("direct", 0, 125)]
    public async Task Refund_tip_wire_accepts_omitted_null_and_minor_unit_values(
        string tipPayload,
        int refundAmountMinor,
        long expectedRefundTipMinor)
    {
        AuthenticateAsAdmin();
        var request = new JsonObject
        {
            ["refundAmount"] = refundAmountMinor / 100m,
            ["refundReason"] = "Test refund"
        };
        if (tipPayload == "null")
            request["refundTipMinor"] = null;
        else if (tipPayload == "direct")
            request["refundTipMinor"] = 125;

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync(
            $"/api/orders/{_orderId}/payments/{_paymentId}/refund", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<OrderPaymentDto>>(JsonOptions);
        body!.Success.Should().BeTrue(body.Message);
        body.Data!.RefundedTipMinor.Should().Be(expectedRefundTipMinor);

        await using var verify = DatabaseFixture.CreateContext();
        var payment = await verify.OrderPayments.SingleAsync(value => value.Id == _paymentId);
        payment.RefundedTipMinor.Should().Be(expectedRefundTipMinor);
        payment.RefundedAmount.Should().Be(refundAmountMinor / 100m);
    }
}
