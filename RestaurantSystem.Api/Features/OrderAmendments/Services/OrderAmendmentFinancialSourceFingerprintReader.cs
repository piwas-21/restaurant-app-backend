using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentFinancialSourceFingerprintReader
{
    internal static async Task<string> ReadAsync(ApplicationDbContext context, Order source,
        OrderAmendment amendment, Guid currentOperationId, AccountMoney money,
        CancellationToken cancellationToken)
    {
        var amendments = await context.OrderAmendments.AsNoTracking()
            .Where(value => value.SourceOrderId == source.Id
                && value.State == OrderAmendmentState.Committed)
            .ToListAsync(cancellationToken);
        if (amendments.Any(value => value.Id != amendment.Id
                && OrderAmendmentFinancialGuard.IsUnresolved(value.FinancialResolutionJson)))
            throw new ConflictException("Resolve the earlier amendment before posting another refund.");

        var attempts = source.ServiceSessionId is not Guid sessionId ? []
            : await context.AccountPaymentAttempts.AsNoTracking()
                .Where(value => value.ServiceSessionId == sessionId
                    && value.Allocations.Any(allocation => allocation.OrderId == source.Id))
                .Include(value => value.Allocations.Where(allocation => allocation.OrderId == source.Id))
                .ToListAsync(cancellationToken);
        var prior = await AccountAmendmentRefundIntegrity.ReadAsync(context, [source], amendments,
            attempts, money, cancellationToken, currentOperationId);
        var attemptIds = attempts.Select(value => value.Id).ToArray();
        var journals = attemptIds.Length == 0 ? []
            : await context.AccountCheckoutJournals.AsNoTracking()
                .Where(value => attemptIds.Contains(value.AttemptId)).ToListAsync(cancellationToken);
        var credits = await context.OrderBillingCredits.AsNoTracking()
            .Where(value => value.SourceOrderId == source.Id).ToListAsync(cancellationToken);
        var loyaltyEvidence = await OrderAmendmentLoyaltyEvidenceReader.ReadAsync(
            context, source.Id, cancellationToken);
        var operationSnapshot = await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .Where(value => value.Id == currentOperationId)
            .Select(value => value.SnapshotJson).SingleOrDefaultAsync(cancellationToken);
        if (operationSnapshot is null)
            throw new ConflictException("The frozen loyalty resolution operation is unavailable.");
        var persistedSnapshot = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionSnapshot>(operationSnapshot);
        var allowAwardAfterSuppression = persistedSnapshot.LoyaltyPlan?.AwardPending == true;
        var loyalty = loyaltyEvidence.Transactions.Where(value => !allowAwardAfterSuppression
            || value.TransactionType != TransactionType.Earned).ToArray();
        return OrderAmendmentFinancialSourceFingerprint.Create(new OrderAmendmentFinancialSourceState(source, amendment, amendments,
            attempts, journals, prior.Reversals, prior.AuthorizedRefundMinorByPayment,
            credits, loyalty, money.Currency,
            OrderAmendmentLoyaltyEvidenceFingerprint.Create(loyaltyEvidence,
                currentOperationId, amendment.Id, allowAwardAfterSuppression)));
    }
}
