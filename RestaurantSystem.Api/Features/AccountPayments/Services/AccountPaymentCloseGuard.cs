using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Closing a visit cannot erase a minor-unit debt or an unresolved payment attempt.</summary>
internal static class AccountPaymentCloseGuard
{
    internal static async Task RequireClosableAsync(
        ApplicationDbContext context, Guid serviceSessionId, ITenantFeatures? features,
        CancellationToken cancellationToken)
    {
        await AccountPaymentLedgerGuard.RequireNoPendingAsync(context, serviceSessionId, cancellationToken);
        await OrderAmendmentFinancialGuard.AssertNoPendingSessionResolutionAsync(
            context, serviceSessionId, cancellationToken);
        var hasCapturedLedger = await context.AccountPaymentAttempts.AsNoTracking()
            .AnyAsync(value => value.ServiceSessionId == serviceSessionId
                && value.State == AccountPaymentState.Captured, cancellationToken);
        if (features?.TableAccountPaymentsV1 != true && !hasCapturedLedger)
        {
            await AccountCheckoutEvidenceGuard.ValidateSessionAsync(
                context, serviceSessionId, cancellationToken);
            return;
        }
        var snapshot = await new AccountDebtSnapshotReader(context).ReadAsync(serviceSessionId, cancellationToken);
        if (snapshot.Debt.OutstandingMinor != 0)
            throw new ConflictException("The account still has an exact outstanding balance. Settle it before closing the visit.");
    }
}
