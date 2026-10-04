using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService
{
    public async Task<OrderAmendmentResolutionContextDto> ContextAsync(
        Guid orderId, Guid amendmentId, CancellationToken cancellationToken)
    {
        RequireAdminActor();
        OrderAmendmentPolicy.RequireFeature(features);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var source = await LoadSourceAsync(orderId, cancellationToken);
        var amendment = await context.OrderAmendments.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == amendmentId && value.SourceOrderId == orderId,
                cancellationToken)
            ?? throw new NotFoundException("The committed amendment was not found.");
        if (await context.OrderAmendmentResolutionOperations.AsNoTracking()
            .AnyAsync(value => value.AmendmentId == amendmentId, cancellationToken))
            throw new ConflictException("This amendment already has a financial resolution operation.");
        var sourceAmendments = await context.OrderAmendments.AsNoTracking()
            .Where(value => value.SourceOrderId == source.Id
                && value.State == OrderAmendmentState.Committed).ToListAsync(cancellationToken);
        if (sourceAmendments.Any(value => value.Id != amendmentId
                && OrderAmendmentFinancialGuard.IsUnresolved(value.FinancialResolutionJson)))
            throw new ConflictException("Resolve the earlier committed amendment before starting another paid correction.");
        if (source.ServiceSessionId is Guid sessionId
            && await context.OrderAmendmentResolutionOperations.AsNoTracking().AnyAsync(value =>
                value.ServiceSessionId == sessionId
                && value.State != OrderAmendmentResolutionOperationState.Resolved, cancellationToken))
            throw new ConflictException("Resolve the earlier paid correction in this table account first.");

        var attempts = await ReadAttemptsAsync(source.ServiceSessionId, source.Id, cancellationToken);
        var loyalty = await context.FidelityPointsTransactions.AsNoTracking()
            .Where(value => value.OrderId == source.Id).ToListAsync(cancellationToken);
        var money = new AccountMoney(source.ServiceSession?.Currency ?? resolutionPolicy.ResolveCurrency(source));
        var refunds = await AccountAmendmentRefundIntegrity.ReadAsync(context, [source], sourceAmendments,
            attempts, money, cancellationToken);
        var credit = OrderAmendmentResolutionPlanner.ValidateSourceForResolution(
            source, amendment, refunds.AuthorizedRefundMinorByPayment, money, loyalty.Count > 0);
        var attemptIds = attempts.Select(value => value.Id).ToArray();
        var journals = attemptIds.Length == 0 ? []
            : await context.AccountCheckoutJournals.AsNoTracking()
                .Where(value => attemptIds.Contains(value.AttemptId)).ToListAsync(cancellationToken);
        var changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson);
        var removals = OrderAmendmentRefundScopePlanner.RemovalRanges(changes);
        OrderAmendmentRefundScopePlanner.EnsureNoPriorRemovalRefund(removals, refunds.Reversals);
        _ = OrderAmendmentResolutionPlanner.PlanAllocatedRefunds(source, attempts, journals,
            refunds.Reversals, removals, money);
        await OrderBillingCreditConsistency.AssertAsync(context, [source.Id], cancellationToken);

        var allocatedPaymentIds = attempts.SelectMany(value => value.Allocations)
            .Where(value => value.OrderId == source.Id && value.OrderPaymentId.HasValue)
            .Select(value => value.OrderPaymentId!.Value).ToHashSet();
        var candidates = source.Payments.Where(payment => payment.Status.IsCaptured()
                && payment.PaymentGateway is null
                && payment.PaymentMethod is PaymentMethod.Cash or PaymentMethod.CreditCard
                && !allocatedPaymentIds.Contains(payment.Id))
            .Select(payment => new ManualRefundCandidateDto(payment.Id,
                payment.PaymentMethod.ToString(), checked(money.ToMinor(payment.Amount)
                    - refunds.AuthorizedRefundMinorByPayment.GetValueOrDefault(payment.Id))))
            .Where(value => value.AvailableMinor > 0)
            .OrderBy(value => value.PaymentId).ToArray();

        var result = new OrderAmendmentResolutionContextDto(source.Id, amendment.Id,
            source.Version, source.ServiceSession?.AccountRevision, money.Currency, credit, candidates);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
