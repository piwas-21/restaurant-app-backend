using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountPaymentRequestRules
{
    private const long MaximumTipMinor = 9_999_999_999;

    internal static string QuoteHash(
        Guid sessionId, CreateAccountPaymentQuoteRequest request, bool allowOnlinePayment = false)
    {
        ValidateQuote(request, allowOnlinePayment);
        var units = request.SelectedUnits.OrderBy(value => value.OrderId)
            .ThenBy(value => value.OrderItemId).ThenBy(value => value.Ordinal).ToArray();
        return AccountPaymentSnapshots.Hash(new
        {
            sessionId,
            request.OperationId,
            request.ExpectedAccountRevision,
            request.Mode,
            request.PaymentMethod,
            selectedUnits = units,
            request.AmountMinor,
            request.EqualSharePlanId,
            request.EqualShareOrdinal,
            request.CustomSharePlanId,
            request.CustomShareOrdinal,
            TipMinor = EffectiveTipMinor(request)
        });
    }

    internal static long EffectiveTipMinor(CreateAccountPaymentQuoteRequest request) =>
        request.TipMinor is long tipMinor ? tipMinor : 0L;

    internal static string PlanHash(Guid sessionId, CreateAccountEqualSharePlanRequest request) =>
        AccountPaymentSnapshots.Hash(new
        {
            sessionId,
            request.OperationId,
            request.ExpectedAccountRevision,
            request.ShareCount,
            customAmountsMinor = request.CustomAmountsMinor.ToArray(),
            request.SupersedesPlanId
        });

    internal static void ValidateQuote(CreateAccountPaymentQuoteRequest request, bool allowOnlinePayment = false)
    {
        var tipMinor = EffectiveTipMinor(request);
        if (request.OperationId == Guid.Empty || request.ExpectedAccountRevision <= 0
            || tipMinor < 0 || tipMinor > MaximumTipMinor
            || !Enum.IsDefined(request.Mode) || !Enum.IsDefined(request.PaymentMethod))
            throw new BadRequestException("A valid operation, account revision, payment mode and method are required.");
        ValidatePaymentMethod(request.PaymentMethod, allowOnlinePayment);
        var units = request.SelectedUnits ?? throw new BadRequestException("Selected units are required.");
        if (!MatchesMode(request, units))
            throw new BadRequestException("The payment request contains fields that do not match its selected mode.");
        if (request.Mode == AccountPaymentMode.Items && HasInvalidItemUnits(units))
            throw new BadRequestException("Select distinct payable item units.");
    }

    private static void ValidatePaymentMethod(PaymentMethod paymentMethod, bool allowOnlinePayment)
    {
        if (paymentMethod is not (PaymentMethod.Cash or PaymentMethod.CreditCard)
            && !(allowOnlinePayment && paymentMethod == PaymentMethod.OnlinePayment))
            throw new BadRequestException("Table account collection currently supports cash or manual card only.");
    }

    private static bool MatchesMode(
        CreateAccountPaymentQuoteRequest request, IReadOnlyList<AccountPaymentUnitSelection> units) =>
        request.Mode switch
        {
            AccountPaymentMode.Items => IsItemsRequest(request, units),
            AccountPaymentMode.Amount => IsAmountRequest(request, units),
            AccountPaymentMode.Full => IsFullRequest(request, units),
            AccountPaymentMode.Equal => IsEqualRequest(request, units),
            AccountPaymentMode.CustomAmount => IsCustomAmountRequest(request, units),
            _ => false
        };

    private static bool IsItemsRequest(
        CreateAccountPaymentQuoteRequest request, IReadOnlyList<AccountPaymentUnitSelection> units) =>
        units.Count > 0 && request.AmountMinor is null && HasNoSharePlan(request);

    private static bool HasInvalidItemUnits(IReadOnlyList<AccountPaymentUnitSelection> units) =>
        units.Any(value => value.OrderId == Guid.Empty || value.OrderItemId == Guid.Empty || value.Ordinal < 1)
        || units.Distinct().Count() != units.Count;

    private static bool IsAmountRequest(
        CreateAccountPaymentQuoteRequest request, IReadOnlyList<AccountPaymentUnitSelection> units) =>
        units.Count == 0 && request.AmountMinor is > 0 && HasNoSharePlan(request);

    private static bool IsFullRequest(
        CreateAccountPaymentQuoteRequest request, IReadOnlyList<AccountPaymentUnitSelection> units) =>
        units.Count == 0 && request.AmountMinor is null && HasNoSharePlan(request);

    private static bool IsEqualRequest(
        CreateAccountPaymentQuoteRequest request, IReadOnlyList<AccountPaymentUnitSelection> units) =>
        units.Count == 0 && request.AmountMinor is null
        && request.EqualSharePlanId is Guid planId && planId != Guid.Empty
        && request.EqualShareOrdinal is > 0
        && request.CustomSharePlanId is null && request.CustomShareOrdinal is null;

    private static bool IsCustomAmountRequest(
        CreateAccountPaymentQuoteRequest request, IReadOnlyList<AccountPaymentUnitSelection> units) =>
        units.Count == 0 && request.AmountMinor is null
        && request.CustomSharePlanId is Guid customPlanId && customPlanId != Guid.Empty
        && request.CustomShareOrdinal is > 0
        && request.EqualSharePlanId is null && request.EqualShareOrdinal is null;

    private static bool HasNoSharePlan(CreateAccountPaymentQuoteRequest request) =>
        request.EqualSharePlanId is null && request.EqualShareOrdinal is null
        && request.CustomSharePlanId is null && request.CustomShareOrdinal is null;

    internal static void RequireCurrentRevision(long current, long expected)
    {
        if (current != expected)
            throw new ConflictException("The account changed. Refresh the account before continuing.");
    }
}
