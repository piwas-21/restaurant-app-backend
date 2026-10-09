using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;

namespace RestaurantSystem.Api.Features.Orders.Services;

internal static partial class OrderBillingSnapshotFactory
{
    private static long ToMinor(AccountMoney money, decimal amount, bool allowSigned)
    {
        if (!allowSigned && amount < 0)
            throw Reconciliation("A nonnegative billing component was negative.");
        try
        {
            if (amount < 0)
                return checked(-money.ToMinor(-amount));
            return money.ToMinor(amount);
        }
        catch (Exception exception) when (exception is OverflowException or BadRequestException)
        {
            throw Reconciliation("An accepted billing amount is not an exact supported minor-unit value.");
        }
    }

    private static long QuantizeToMinor(AccountMoney money, decimal amount, bool allowSigned)
    {
        try
        {
            var quantized = decimal.Round(amount, OrderBillingSnapshotLimits.CurrencyMinorDigits,
                MidpointRounding.AwayFromZero);
            return ToMinor(money, quantized, allowSigned);
        }
        catch (OverflowException)
        {
            throw Reconciliation("The accepted billing component exceeds the supported minor-unit range.");
        }
    }

    private static long CalculateFoodReconciliation(
        long payableFood, long gross, long tax, long orderDiscount,
        long customerDiscount, long rounding, long redemptionDiscount)
    {
        try
        {
            var components = checked(gross - tax - orderDiscount - customerDiscount
                + rounding - redemptionDiscount);
            return checked(payableFood - components);
        }
        catch (OverflowException)
        {
            throw Reconciliation("The accepted food components exceed the supported integer range.");
        }
    }

    private static void ValidateFoodTotal(long food, long fee, long tip, long total)
    {
        try
        {
            if (checked(food + fee + tip) != total)
                throw Reconciliation("The accepted total does not conserve payable food, charged fee and tip.");
        }
        catch (OverflowException)
        {
            throw Reconciliation("The accepted total exceeds the supported integer range.");
        }
    }

    private static long SumMinor(IEnumerable<long> values)
    {
        try
        {
            return values.Aggregate(0L, (sum, value) => checked(sum + value));
        }
        catch (OverflowException)
        {
            throw Reconciliation("The accepted billing components exceed the supported integer range.");
        }
    }

    private static ConflictException Reconciliation(string message) => new(message);

}
