using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Separates retained gratuity/fees from the frozen food units used by amendments.</summary>
internal static class FrozenOrderChargeMath
{
    internal static FrozenOrderCharge Read(Order order, AccountMoney money)
    {
        var total = money.ToMinor(order.Total);
        var tip = money.ToMinor(Math.Max(0m, order.Tip));
        if (tip > total)
            throw new ConflictException("The frozen tip exceeds the order charge. Reconciliation is required.");
        // A sale discount can reduce the fee's charged value; never invent an uncharged fee.
        var fee = Math.Min(money.ToMinor(Math.Max(0m, order.DeliveryFee)), total - tip);
        var roots = order.Items.Where(item => item.ParentOrderItemId is null)
            .OrderBy(item => item.Id).ToArray();
        if (roots.Any(item => item.Quantity <= 0 || item.OrderId != order.Id)
            || roots.Select(item => item.Id).Distinct().Count() != roots.Length)
            throw new ConflictException("The order contains invalid frozen food lines.");
        var weights = roots.Select(item => money.ToMinor(item.ItemTotal)).ToArray();
        if (weights.Length > 0 && weights.All(weight => weight == 0))
            weights = roots.Select(item => (long)item.Quantity).ToArray();
        var food = total - tip - fee;
        var shares = roots.Length == 0 ? [] : AccountShareMath.Weighted(food, weights);
        return new(total, food, tip, fee,
            roots.Select((item, index) => new FrozenFoodCharge(item, shares[index])).ToArray());
    }

    internal static long RemovedUnits(FrozenFoodCharge line, int startOrdinal, int count)
    {
        if (startOrdinal < 1 || count < 1 || (long)startOrdinal + count - 1 > line.Item.Quantity)
            throw new BadRequestException("The removed units must fit the frozen food line.");
        var each = line.AmountMinor / line.Item.Quantity;
        var remainder = line.AmountMinor % line.Item.Quantity;
        var end = (long)startOrdinal + count - 1;
        var higherUnits = Math.Max(0L, Math.Min(end, remainder) - startOrdinal + 1);
        return checked(each * count + higherUnits);
    }
}

internal sealed record FrozenFoodCharge(OrderItem Item, long AmountMinor);
internal sealed record FrozenOrderCharge(
    long TotalMinor, long FoodMinor, long TipMinor, long FeeMinor,
    IReadOnlyList<FrozenFoodCharge> FoodLines);
