using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class AccountSharePlanRules
{
    internal static void RequireRequest(Guid sessionId, CreateAccountEqualSharePlanRequest request, AccountPaymentSettings limits)
    {
        if (sessionId == Guid.Empty || request.OperationId == Guid.Empty
            || request.ExpectedAccountRevision <= 0 || request.CustomAmountsMinor is null)
            throw new BadRequestException("A valid table visit, operation and account revision are required.");
        if (request.ShareCount < 2 || request.ShareCount > limits.MaximumEqualShares)
            throw new BadRequestException("The equal-share count is outside the configured limit.");
        if (request.SupersedesPlanId == Guid.Empty)
            throw new BadRequestException("The superseded plan id is invalid.");
        if (request.CustomAmountsMinor.Count > 0
            && (request.CustomAmountsMinor.Count != request.ShareCount
                || request.CustomAmountsMinor.Any(value => value <= 0 || value > 9_999_999_999)))
            throw new BadRequestException("Every custom guest amount must be positive and match the guest count.");
    }

    internal static void RequireScope(IReadOnlyList<AccountDebtSegment> scope, long total,
        CreateAccountEqualSharePlanRequest request, long roundingIncrement, AccountPaymentSettings limits)
    {
        if (scope.Count == 0)
            throw new BadRequestException("The reviewed table balance has no payable value.");
        if (request.CustomAmountsMinor.Count == 0)
        {
            var distribution = AccountShareMath.Equal(total, request.ShareCount, roundingIncrement);
            if (Enumerable.Range(1, request.ShareCount).Any(ordinal => distribution.At(ordinal) <= 0))
                throw new BadRequestException(
                    "The balance is too small for positive equal shares at this currency's cash increment.");
        }
        if (request.CustomAmountsMinor.Count > 0)
        {
            long customTotal;
            try
            {
                customTotal = request.CustomAmountsMinor.Aggregate(0L, (sum, amount) => checked(sum + amount));
            }
            catch (OverflowException)
            {
                throw new BadRequestException("The custom guest amounts exceed the supported balance.");
            }
            if (customTotal != total)
                throw new BadRequestException("Custom guest amounts must add up to the full available table balance.");
        }
        if (scope.Count > limits.MaximumScopeSegments)
            throw new BadRequestException("The equal-share scope exceeds the configured segment limit.");
    }
}
