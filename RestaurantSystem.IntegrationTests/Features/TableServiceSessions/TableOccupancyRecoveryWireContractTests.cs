using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;

namespace RestaurantSystem.IntegrationTests.Features.TableServiceSessions;

public sealed class TableOccupancyRecoveryWireContractTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 5.25)]
    [InlineData(true, 5.25)]
    public void Preview_and_saved_receipt_omit_zero_charge_but_retain_positive_charge(
        bool savedReceipt, decimal chargeAmount)
    {
        var order = new TableOccupancyRecoveryOrderDto(
            Guid.NewGuid(), "RECOVERY-1", "RetainedInPriorVisit", "Preparing", "Pending",
            10m, 0m, 0m, 10m, false, true, true)
        {
            OriginalBillChargeAmount = chargeAmount
        };
        object payload = savedReceipt
            ? new TableOccupancyRecoveryOperationDto(
                Guid.NewGuid(), Guid.NewGuid(), null, "Verified release", DateTime.UnixEpoch,
                DateTime.UnixEpoch, "NeedsReset", 2, null, null, 0, 0, 1, 0m, 10m, [order])
            : new TableOccupancyRecoveryPreviewDto(
                Guid.NewGuid(), "1", null, null, null, 1, "CHF", "preview", 1,
                0, 0, 1, 1, 0, 0, 0, 0, 0, 10m, [order]);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var wireOrder = json.RootElement.GetProperty("orders")[0];

        wireOrder.GetProperty("originalTotal").GetDecimal().Should().Be(10m);
        order.OriginalBillChargeAmount.Should().Be(chargeAmount);
        if (chargeAmount == 0)
            wireOrder.TryGetProperty("originalBillChargeAmount", out _).Should().BeFalse(
                "existing strict clients must accept recovery while no charges are configured");
        else
            wireOrder.GetProperty("originalBillChargeAmount").GetDecimal().Should().Be(5.25m,
                "a saved positive charge remains explicit in previews and recovery receipts");
    }
}
