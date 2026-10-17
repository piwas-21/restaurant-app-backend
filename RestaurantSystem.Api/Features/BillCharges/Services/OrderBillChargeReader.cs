using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.BillCharges.Services;

internal static class OrderBillChargeReader
{
    internal static IReadOnlyList<OrderBillCharge> Read(Order order)
    {
        try
        {
            var charges = JsonSerializer.Deserialize<List<OrderBillCharge>>(order.BillChargesJson, BillChargeRules.JsonOptions) ?? [];
            if (charges.Any(c => c is null || c.Amount <= 0 || string.IsNullOrWhiteSpace(c.Key) || string.IsNullOrWhiteSpace(c.Label))
                || charges.Select(c => c.Key).Distinct().Count() != charges.Count
                || charges.Sum(c => c.Amount) != order.BillChargeAmount)
                throw new ConflictException("The bill charge audit records need verification.");
            return charges;
        }
        catch (JsonException)
        {
            throw new ConflictException("The bill charge audit records could not be read.");
        }
    }
}
