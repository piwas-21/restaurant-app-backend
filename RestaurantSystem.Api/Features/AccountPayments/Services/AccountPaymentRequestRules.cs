using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountPaymentRequestRules
{
    internal static string QuoteHash(Guid sessionId, CreateAccountPaymentQuoteRequest request)
    {
        ValidateQuote(request);
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
            request.EqualShareOrdinal
        });
    }

    internal static string PlanHash(Guid sessionId, CreateAccountEqualSharePlanRequest request) =>
        AccountPaymentSnapshots.Hash(new
        {
            sessionId,
            request.OperationId,
            request.ExpectedAccountRevision,
            request.ShareCount,
            request.SupersedesPlanId
        });

    internal static void ValidateQuote(CreateAccountPaymentQuoteRequest request)
    {
        if (request.OperationId == Guid.Empty || request.ExpectedAccountRevision <= 0
            || !Enum.IsDefined(request.Mode) || !Enum.IsDefined(request.PaymentMethod))
            throw new BadRequestException("A valid operation, account revision, payment mode and method are required.");
        if (request.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.CreditCard))
            throw new BadRequestException("Table account collection currently supports cash or manual card only.");

        var units = request.SelectedUnits ?? throw new BadRequestException("Selected units are required.");
        switch (request.Mode)
        {
            case AccountPaymentMode.Items when units.Count > 0 && request.AmountMinor is null
                && request.EqualSharePlanId is null && request.EqualShareOrdinal is null:
                if (units.Any(value => value.OrderId == Guid.Empty || value.OrderItemId == Guid.Empty || value.Ordinal < 1)
                    || units.Distinct().Count() != units.Count)
                    throw new BadRequestException("Select distinct payable item units.");
                break;
            case AccountPaymentMode.Amount when units.Count == 0 && request.AmountMinor is > 0
                && request.EqualSharePlanId is null && request.EqualShareOrdinal is null:
                break;
            case AccountPaymentMode.Equal when units.Count == 0 && request.AmountMinor is null
                && request.EqualSharePlanId is Guid planId && planId != Guid.Empty
                && request.EqualShareOrdinal is > 0:
                break;
            default:
                throw new BadRequestException("The payment request contains fields that do not match its selected mode.");
        }
    }

    internal static void RequireCurrentRevision(long current, long expected)
    {
        if (current != expected)
            throw new ConflictException("The account changed. Refresh the account before continuing.");
    }
}
