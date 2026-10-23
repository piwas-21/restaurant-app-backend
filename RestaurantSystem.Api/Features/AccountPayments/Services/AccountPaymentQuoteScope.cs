using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

// Shared selection only; neither a quote nor a reservation is persisted here.
internal static class AccountPaymentQuoteScope
{
    internal static async Task<IReadOnlyList<AccountDebtSegment>> SelectAsync(
        ApplicationDbContext context,
        Guid sessionId, CreateAccountPaymentQuoteRequest request,
        AccountPaymentAccountSnapshot account, CancellationToken cancellationToken)
    {
        var segments = request.Mode switch
        {
            AccountPaymentMode.Items => ReadItemSegments(request, account),
            AccountPaymentMode.Amount => AccountDebtMath.Amount(account.Debt.Available, request.AmountMinor!.Value),
            AccountPaymentMode.Full => account.Debt.Available,
            AccountPaymentMode.Equal or AccountPaymentMode.CustomAmount => await ReadShareSegmentsAsync(
                context, sessionId, request, cancellationToken),
            _ => throw new BadRequestException("The selected payment mode is not supported.")
        };

        RequirePayableScope(account.Debt.Available, segments);
        return segments;
    }

    private static IReadOnlyList<AccountDebtSegment> ReadItemSegments(
        CreateAccountPaymentQuoteRequest request, AccountPaymentAccountSnapshot account) =>
        AccountDebtMath.Items(account.Debt.Available,
            request.SelectedUnits.Select(value => new AccountUnitIdentity(
                value.OrderId, value.OrderItemId, value.Ordinal)).ToArray());

    private static async Task<IReadOnlyList<AccountDebtSegment>> ReadShareSegmentsAsync(
        ApplicationDbContext context, Guid sessionId, CreateAccountPaymentQuoteRequest request, CancellationToken cancellationToken)
    {
        var isCustom = request.Mode == AccountPaymentMode.CustomAmount;
        var planId = isCustom ? request.CustomSharePlanId : request.EqualSharePlanId;
        var ordinal = isCustom ? request.CustomShareOrdinal : request.EqualShareOrdinal;
        var plan = await context.AccountEqualSharePlans.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == planId && value.ServiceSessionId == sessionId,
            cancellationToken) ?? throw new NotFoundException("The equal-share plan was not found.");
        if (plan.InvalidatedAt is not null)
            throw new ConflictException("The equal-share plan is no longer available to this cashier.");
        if ((plan.CustomAmountsJson is not null) != isCustom)
            throw new ConflictException("The selected guest split plan does not match the payment mode.");
        if (ordinal > plan.ShareCount)
            throw new BadRequestException("The equal-share position is outside the reviewed plan.");
        var scope = AccountPaymentSnapshots.ReadScope(plan.ScopeJson);
        return isCustom
            ? AccountCustomShareScopeMath.ForShare(scope,
                AccountPaymentSnapshots.Deserialize<List<long>>(plan.CustomAmountsJson!), ordinal!.Value)
            : AccountEqualScopeMath.ForShare(
                scope, plan.ShareCount, ordinal!.Value, plan.RoundingIncrementMinor);
    }

    private static void RequirePayableScope(
        IReadOnlyList<AccountDebtSegment> available, IReadOnlyList<AccountDebtSegment> segments)
    {
        // A quote is not a reservation. Keep the exact reviewed scope and fail rather than silently
        // shifting it if another contribution reserved or captured any of these units meanwhile.
        AccountDebtMath.Subtract(available, segments);
        if (segments.Count == 0 || AccountDebtMath.Total(segments) <= 0)
        {
            throw new BadRequestException("The payment scope has no payable value.");
        }
    }

}
