using System.Numerics;
using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.BillCharges.Services;

internal static class OrderBillChargeReader
{
    internal static IReadOnlyList<OrderBillCharge> Read(Order order)
    {
        try
        {
            var charges = JsonSerializer.Deserialize<List<OrderBillCharge>>(order.BillChargesJson, BillChargeRules.JsonOptions) ?? [];
            if (charges.Any(Invalid)
                || charges.Select(c => c.Key).Distinct().Count() != charges.Count
                || charges.Sum(c => c.Amount) != order.BillChargeAmount)
                throw new ConflictException("The bill charge audit records need verification.");
            return charges;
        }
        catch (Exception exception) when (exception is JsonException or BadRequestException or OverflowException)
        {
            throw new ConflictException("The bill charge audit records could not be read.");
        }
    }

    private static bool Invalid(OrderBillCharge charge)
    {
        if (charge is null || charge.Amount == 0 || string.IsNullOrWhiteSpace(charge.Key) || string.IsNullOrWhiteSpace(charge.Label)
            || charge.Effect is not ("Increase" or "Reduce") || charge.Calculation is not ("Fixed" or "Percentage")
            || charge.Amount < 0 != (charge.Effect == "Reduce")) return true;
        if (!charge.EnteredValue.HasValue)
            return charge.Effect != "Increase" || charge.Calculation != "Fixed" || charge.Allocations is { Count: > 0 };
        var money = new AccountMoney(charge.Currency);
        if (charge.EnteredValue is <= 0 || charge.EnteredValue > (charge.Calculation == "Percentage" ? 100m : BillChargeRules.MaximumCharge)
            || charge.BasisMinor is null or < 0) return true;
        var magnitude = money.ToMinor(Math.Abs(charge.Amount));
        var expected = charge.Calculation == "Percentage"
            ? decimal.Round(charge.BasisMinor.Value * charge.EnteredValue.Value / 100m, 0, MidpointRounding.AwayFromZero)
            : money.ToMinor(charge.EnteredValue.Value);
        if (magnitude != expected) return true;
        if (charge.Effect == "Increase") return charge.Allocations is { Count: > 0 };
        return charge.BasisMinor == 0 || charge.Allocations is not { Count: > 0 }
            || charge.Allocations.Any(value => value is null || value.OrderItemId == Guid.Empty
                || value.StartOrdinal < 1 || value.Count <= 0 || value.MinorPerUnit <= 0
                || (long)value.StartOrdinal + value.Count > (long)int.MaxValue + 1)
            || charge.Allocations.Aggregate(BigInteger.Zero, (sum, value) => sum + (BigInteger)value.Count * value.MinorPerUnit) != magnitude;
    }
}
