using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService
{
    private async Task<OrderAmendmentResolutionPlanningState> ReadPlanningStateAsync(
        Guid orderId, Guid amendmentId, OrderAmendmentResolutionQuoteRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            : null;
        var source = await LoadSourceAsync(orderId, cancellationToken);
        var amendment = await context.OrderAmendments.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == amendmentId
                && value.SourceOrderId == orderId, cancellationToken)
            ?? throw new NotFoundException("The committed amendment was not found.");
        if (await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .AnyAsync(value => value.AmendmentId == amendmentId, cancellationToken))
            throw new ConflictException("This amendment already has a financial resolution operation.");
        var changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson);
        var sourceAmendments = await context.OrderAmendments.AsNoTracking()
            .Where(value => value.SourceOrderId == source.Id
                && value.State == OrderAmendmentState.Committed)
            .ToListAsync(cancellationToken);
        if (sourceAmendments.Any(value => value.Id != amendmentId
                && OrderAmendmentFinancialGuard.IsUnresolved(value.FinancialResolutionJson)))
            throw new ConflictException("Resolve the earlier committed amendment before starting another paid correction.");
        if (source.ServiceSessionId is Guid sessionId
            && await context.OrderAmendmentResolutionOperations.AsNoTracking().AnyAsync(value =>
                value.ServiceSessionId == sessionId
                && value.State != OrderAmendmentResolutionOperationState.Resolved, cancellationToken))
            throw new ConflictException("Resolve the earlier paid correction in this table account first.");
        var attempts = await ReadAttemptsAsync(source.ServiceSessionId, source.Id, cancellationToken);
        var loyaltyTransactions = await context.FidelityPointsTransactions.AsNoTracking()
            .Where(value => value.OrderId == source.Id).ToListAsync(cancellationToken);
        var hasLoyaltyLedgerHistory = loyaltyTransactions.Count > 0;
        var money = new AccountMoney(source.ServiceSessionId.HasValue
            ? source.ServiceSession?.Currency : request.Currency);
        var priorRefunds = await AccountAmendmentRefundIntegrity.ReadAsync(
            context, [source], sourceAmendments, attempts, money, cancellationToken);
        var attemptIds = attempts.Select(value => value.Id).ToArray();
        var journals = attemptIds.Length == 0 ? []
            : await context.AccountCheckoutJournals.AsNoTracking()
                .Where(value => attemptIds.Contains(value.AttemptId)).ToListAsync(cancellationToken);
        var credits = await context.OrderBillingCredits.AsNoTracking()
            .Where(value => value.SourceOrderId == source.Id).ToListAsync(cancellationToken);
        var plan = OrderAmendmentResolutionPlanner.Build(source, amendment, request, changes,
            attempts, journals, priorRefunds.Reversals,
            priorRefunds.AuthorizedRefundMinorByPayment, money, hasLoyaltyLedgerHistory);
        var sourceFingerprint = OrderAmendmentFinancialSourceFingerprint.Create(
            source, amendment, sourceAmendments, attempts, journals, priorRefunds.Reversals,
            priorRefunds.AuthorizedRefundMinorByPayment, credits, loyaltyTransactions, money.Currency);
        return new OrderAmendmentResolutionPlanningState(
            source, amendment, changes, attempts, journals, priorRefunds.Reversals, plan,
            sourceFingerprint);
    }

    private async Task<Order> LoadSourceAsync(Guid orderId, CancellationToken cancellationToken) =>
        await context.Orders.AsNoTracking()
            .Where(value => value.Id == orderId && !value.IsDeleted)
            .Include(value => value.ServiceSession)
            .Include(value => value.Items)
            .Include(value => value.Payments)
            .Include(value => value.ExternalReference)
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("The amendment source order was not found.");

    private async Task<IReadOnlyList<AccountPaymentAttempt>> ReadAttemptsAsync(
        Guid? serviceSessionId, Guid sourceOrderId, CancellationToken cancellationToken)
    {
        if (serviceSessionId is not Guid sessionId)
            return [];
        return await context.AccountPaymentAttempts.AsNoTracking()
            .Where(value => value.ServiceSessionId == sessionId
                && value.Allocations.Any(allocation => allocation.OrderId == sourceOrderId))
            .Include(value => value.Allocations.Where(allocation => allocation.OrderId == sourceOrderId))
            .ToListAsync(cancellationToken);
    }
}
