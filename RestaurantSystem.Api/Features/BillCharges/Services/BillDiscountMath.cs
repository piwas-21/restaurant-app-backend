using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.BillCharges.Services;

/// <summary>Retains the accepted food-unit prices after immutable staff reductions.</summary>
internal static class BillDiscountMath
{
    internal static decimal ItemBasis(Order source)
    {
        if (!source.Items.Any(value => value.ParentOrderItemId is null)) return 0m;
        var tip = Math.Max(0m, source.Tip);
        var fee = Math.Min(Math.Max(0m, source.DeliveryFee), Math.Max(0m, source.Total - tip));
        var reductions = OrderBillChargeReader.Read(source).Where(value => value.Amount < 0).Sum(value => value.Amount);
        return Math.Max(0m, source.Total - tip - fee + reductions - source.BillingCreditAmount);
    }

    internal static IReadOnlyList<AccountDebtSegment> Food(Order order, AccountMoney money)
    {
        var gross = FrozenOrderChargeMath.Read(order, money).FoodLines.SelectMany(line =>
            AccountDebtMath.CreateLine(order.Id, line.Item.Id, line.Item.Quantity, line.AmountMinor, 0)).ToArray();
        var reductions = new List<AccountDebtSegment>();
        foreach (var charge in OrderBillChargeReader.Read(order).Where(value => value.Amount < 0))
        {
            if (charge.Currency != money.Currency)
                throw new ConflictException("The discount currency differs from the accepted bill.");
            var allocations = charge.Allocations!.Select(value => new AccountDebtSegment(order.Id,
                value.OrderItemId, value.StartOrdinal, value.Count, value.MinorPerUnit)).ToArray();
            if (AccountDebtMath.Total(allocations) != money.ToMinor(-charge.Amount))
                throw new ConflictException("The discount allocation differs from its accepted amount.");
            reductions.AddRange(allocations);
        }
        return AccountDebtMath.Subtract(gross, reductions);
    }

    internal static long Removed(Order source, AccountMoney money, Guid itemId, int start, int count)
    {
        var line = source.Items.SingleOrDefault(value => value.Id == itemId && value.ParentOrderItemId is null);
        if (line is null || start < 1 || count < 1 || (long)start + count - 1 > line.Quantity)
            throw new BadRequestException("Choose valid food units to remove.");
        return Removed(Food(source, money), itemId, start, count);
    }

    internal static long Removed(IReadOnlyList<AccountDebtSegment> food, Guid itemId, int start, int count)
    {
        var end = checked((long)start + count);
        return food.Where(value => value.OrderItemId == itemId)
            .Sum(value => checked(Math.Max(0L, Math.Min(end, value.EndExclusive)
                - Math.Max(start, value.StartOrdinal)) * value.MinorPerUnit));
    }

    internal static IReadOnlyList<BillDiscountAllocation> Allocate(
        IReadOnlyList<AccountDebtSegment> food, long amount)
    {
        var total = AccountDebtMath.Total(food);
        if (amount <= 0 || amount > total)
            throw new BadRequestException("The discount must fit the retained item total; fees and tips are excluded.");
        var shares = AccountShareMath.Weighted(amount, food.Select(value => value.TotalMinor).ToArray());
        var result = new List<BillDiscountAllocation>();
        for (var index = 0; index < food.Count; index++)
        {
            var segment = food[index];
            var each = shares[index] / segment.Count;
            var extra = checked((int)(shares[index] % segment.Count));
            if (extra > 0)
                result.Add(new(segment.OrderItemId!.Value, segment.StartOrdinal, extra, each + 1));
            if (each > 0 && extra < segment.Count)
                result.Add(new(segment.OrderItemId!.Value, segment.StartOrdinal + extra, segment.Count - extra, each));
        }
        return result;
    }
}
