using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Resolves explicit tender-level cash rounding in integer minor units.</summary>
internal static class AccountCashSettlementPolicy
{
    internal const string SwissCashFiveRappenV1 = "chf-cash-5-rappen-v1";
    internal const string ExactV1 = "exact-v1";
    internal const long SwissCashIncrementMinor = AccountPaymentSettings.SwissCashFiveRappenV1IncrementMinor;

    internal static CashSettlementQuote ResolveConfigured(
        string? currency, PaymentMethod method, long exactAmountMinor, AccountPaymentSettings settings)
    {
        if (settings.SwissCashPolicyVersion != SwissCashFiveRappenV1
            || settings.SwissCashIncrementMinor != SwissCashIncrementMinor)
            throw new ConflictException("The configured cash settlement policy is unsupported.");
        return Resolve(currency, method, exactAmountMinor);
    }

    // Frozen receipts always decode by version, independently of current configuration.
    internal static CashSettlementQuote Resolve(string? currency, PaymentMethod method, long exactAmountMinor)
    {
        if (exactAmountMinor <= 0 || !Enum.IsDefined(method))
            throw new BadRequestException("A positive amount and defined payment method are required.");

        var normalizedCurrency = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 || normalizedCurrency.Any(value => value is < 'A' or > 'Z'))
            throw new BadRequestException("A three-letter currency code is required.");
        if (method != PaymentMethod.Cash || normalizedCurrency != "CHF")
            return Exact(normalizedCurrency, method, exactAmountMinor);

        long due;
        try
        {
            due = SwissCashDue(exactAmountMinor);
        }
        catch (OverflowException)
        {
            throw new BadRequestException("The cash amount exceeds the supported minor-unit range.");
        }
        var adjustment = due - exactAmountMinor;
        if (due == 0)
            throw new BadRequestException(
                "This cash amount rounds to zero; combine it with another item or use an exact payment method.");

        return new CashSettlementQuote(
            SwissCashFiveRappenV1, normalizedCurrency, method, exactAmountMinor, adjustment, due);
    }

    internal static void RequireMatches(
        CashSettlementQuote frozen, string? currency, PaymentMethod method, long exactAmountMinor)
    {
        if (frozen is null || exactAmountMinor <= 0)
            throw new ConflictException("The frozen cash settlement terms require reconciliation.");
        CashSettlementQuote resolved;
        try
        {
            resolved = Resolve(currency, method, exactAmountMinor);
        }
        catch (Exception exception) when (exception is BadRequestException or OverflowException)
        {
            throw new ConflictException("The frozen cash settlement terms require reconciliation.", exception);
        }
        if (frozen != resolved)
            throw new ConflictException("The frozen cash settlement terms require reconciliation.");
    }

    internal static long RetainedDue(CashSettlementQuote frozenQuote, long retainedExactMinor)
    {
        if (frozenQuote is null || retainedExactMinor < 0
            || retainedExactMinor > frozenQuote.ExactAmountMinor || frozenQuote.ExactAmountMinor <= 0
            || !Enum.IsDefined(frozenQuote.PaymentMethod)
            || string.IsNullOrWhiteSpace(frozenQuote.Currency)
            || frozenQuote.Currency.Length != 3
            || frozenQuote.Currency.Any(value => value is < 'A' or > 'Z')
            || frozenQuote.DueAmountMinor <= 0
            || frozenQuote.AdjustmentMinor != frozenQuote.DueAmountMinor - frozenQuote.ExactAmountMinor
            || ResolveFrozenDue(frozenQuote.PolicyVersion, frozenQuote.Currency,
                frozenQuote.PaymentMethod, frozenQuote.ExactAmountMinor) != frozenQuote.DueAmountMinor)
            throw new ConflictException("The original cash settlement terms require reconciliation.");

        return ResolveFrozenDue(frozenQuote.PolicyVersion, frozenQuote.Currency,
            frozenQuote.PaymentMethod, retainedExactMinor);
    }

    private static long ResolveFrozenDue(string policyVersion, string currency, PaymentMethod method, long exactMinor) =>
        policyVersion switch
        {
            ExactV1 when exactMinor >= 0 && (method != PaymentMethod.Cash || currency != "CHF") => exactMinor,
            SwissCashFiveRappenV1 when currency == "CHF" && method == PaymentMethod.Cash && exactMinor >= 0 =>
                SwissCashDue(exactMinor),
            _ => throw new ConflictException("The original cash policy version is unavailable for reconciliation.")
        };

    private static long SwissCashDue(long exactMinor)
    {
        var remainder = exactMinor % SwissCashIncrementMinor;
        var adjustment = remainder switch
        {
            0 => 0,
            1 or 2 => -remainder,
            _ => SwissCashIncrementMinor - remainder
        };
        return checked(exactMinor + adjustment);
    }

    private static CashSettlementQuote Exact(string currency, PaymentMethod method, long exactAmountMinor) =>
        new(ExactV1, currency, method, exactAmountMinor, 0, exactAmountMinor);
}
