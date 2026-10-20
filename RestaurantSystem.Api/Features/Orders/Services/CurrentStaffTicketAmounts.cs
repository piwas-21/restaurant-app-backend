using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.BillCharges.Dtos;
using RestaurantSystem.Api.Features.BillCharges.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Current invoice amounts retain the exact ordinal portions, after financial corrections settle.</summary>
internal static class CurrentStaffTicketAmounts
{
    internal static void Apply(Order source, OrderDto current, IReadOnlyList<OrderAmendment> amendments)
    {
        if (amendments.Count == 0) return;
        current.HasPendingBillingCorrection = amendments.Any(value => OrderAmendmentFinancialGuard.IsUnresolved(value.FinancialResolutionJson));
        if (current.HasPendingBillingCorrection) return;
        if (!source.Items.Any(value => value.ParentOrderItemId is null))
        {
            current.HasPendingBillingCorrection = true;
            return;
        }
        var money = new AccountMoney(current.Currency);
        var frozen = FrozenOrderChargeMath.Read(source, money);
        var grossFood = frozen.FoodLines.SelectMany(line => AccountDebtMath.CreateLine(
            source.Id, line.Item.Id, line.Item.Quantity, line.AmountMinor, 0)).ToArray();
        var retainedFood = Retained(source, grossFood, amendments);
        var charges = RetainedCharges(source, money, amendments);
        var subtotal = RetainedComponent(source, money.ToMinor(source.SubTotal), money, amendments);
        var tax = RetainedComponent(source, money.ToMinor(source.Tax), money, amendments);
        var saleDiscount = checked(subtotal + tax - retainedFood);
        var expected = money.ToMajor(checked(retainedFood + frozen.TipMinor + frozen.FeeMinor)) + charges.Sum(value => value.Amount);
        if (saleDiscount < 0 || expected != source.PayableTotal)
        {
            current.HasPendingBillingCorrection = true;
            return;
        }
        current.BillCharges = charges;
        current.BillChargeAmount = charges.Sum(charge => charge.Amount);
        current.Total = source.PayableTotal - current.BillChargeAmount;
        current.SubTotal = money.ToMajor(subtotal);
        // The operational invoice combines original sale-discount categories; their frozen audit remains separate.
        current.Discount = money.ToMajor(saleDiscount);
        current.CustomerDiscountAmount = 0m;
        current.FidelityPointsDiscount = 0m;
        current.Tax = money.ToMajor(tax);
        current.DeliveryFee = money.ToMajor(frozen.FeeMinor);
        current.Tip = money.ToMajor(frozen.TipMinor);
    }

    private static List<BillChargeDto> RetainedCharges(Order source, AccountMoney money, IReadOnlyList<OrderAmendment> amendments) =>
        OrderBillChargeReader.Read(source).Select(charge =>
        {
            if (charge.Amount > 0) return new BillChargeDto(charge.Key, charge.Label, charge.Amount, charge.Currency);
            var allocated = charge.Allocations!.Select(value => new AccountDebtSegment(source.Id,
                value.OrderItemId, value.StartOrdinal, value.Count, value.MinorPerUnit)).ToArray();
            return new BillChargeDto(charge.Key, charge.Label, -money.ToMajor(Retained(source, allocated, amendments)), charge.Currency);
        }).Where(charge => charge.Amount != 0).ToList();

    private static long RetainedComponent(Order source, long amount, AccountMoney money, IReadOnlyList<OrderAmendment> amendments)
    {
        if (amount == 0) return 0;
        var roots = source.Items.Where(value => value.ParentOrderItemId is null).OrderBy(value => value.Id).ToArray();
        var weights = roots.Select(value => money.ToMinor(value.ItemTotal)).ToArray();
        if (weights.All(value => value == 0)) weights = roots.Select(value => (long)value.Quantity).ToArray();
        var shares = AccountShareMath.Weighted(amount, weights);
        var portions = roots.SelectMany((item, index) => AccountDebtMath.CreateLine(
            source.Id, item.Id, item.Quantity, shares[index], 0)).ToArray();
        return Retained(source, portions, amendments);
    }

    private static long Retained(Order source, IReadOnlyList<AccountDebtSegment> portions, IReadOnlyList<OrderAmendment> amendments) =>
        AccountDebtMath.Total(AccountDebtAmendmentProjection.ExcludeVoidedUnits([source], portions, amendments));
}
