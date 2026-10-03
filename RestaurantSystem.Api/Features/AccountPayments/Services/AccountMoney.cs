using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Payments.Services;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Uses the existing currency rail; persisted amounts must already fit its minor unit.</summary>
internal sealed class AccountMoney
{
    private readonly long _minorUnitsPerMajor;
    internal string Currency { get; }

    internal AccountMoney(string? currency)
    {
        var unit = CheckoutAmount.From(1m, currency);
        Currency = unit.Currency.ToUpperInvariant();
        _minorUnitsPerMajor = unit.Minor;
    }

    internal long ToMinor(decimal amount)
    {
        var scaled = amount * _minorUnitsPerMajor;
        if (scaled != decimal.Truncate(scaled) || scaled < 0 || scaled > long.MaxValue)
            throw new BadRequestException("The account amount must be a nonnegative exact minor-unit value.");
        return decimal.ToInt64(scaled);
    }

    internal decimal ToMajor(long amountMinor)
    {
        if (amountMinor < 0)
            throw new BadRequestException("A contribution cannot have a negative amount.");
        return (decimal)amountMinor / _minorUnitsPerMajor;
    }
}
