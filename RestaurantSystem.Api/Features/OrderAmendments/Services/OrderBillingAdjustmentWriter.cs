using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

/// <summary>Stages a durable credit inside the amendment's locked order/visit transaction.</summary>
public sealed class OrderBillingAdjustmentWriter(
    ApplicationDbContext context, ICurrentUserService currentUser) : IOrderBillingAdjustmentWriter
{
    public async Task StageUnpaidCreditAsync(Order source, OrderAmendment amendment,
        OrderAmendmentFinancialPreviewDto preview, CancellationToken cancellationToken)
    {
        var actorId = OrderAmendmentPolicy.RequireActor(currentUser);
        if (context.Database.CurrentTransaction is null || source.Id != amendment.SourceOrderId
            || source.Id == Guid.Empty || amendment.Id == Guid.Empty
            || amendment.State != OrderAmendmentState.Committed
            || amendment.ActorUserId != actorId || amendment.ActorRole != currentUser.Role?.ToString()
            || amendment.ServiceSessionId != source.ServiceSessionId)
            throw new ConflictException("A billing credit requires its original amendment transaction and actor.");

        if (preview.Currency is null || preview.AddedAmountMinor < 0 || preview.PotentialCreditMinor <= 0
            || preview.PotentialCreditMinor != preview.RemovedUnitValueMinor
            || preview.NetAccountDeltaMinor != checked(preview.AddedAmountMinor - preview.RemovedUnitValueMinor)
            || preview.ResolutionStatus != OrderAmendmentFinancialResolutionStatus.Resolved
            || preview.CreditState != OrderAmendmentCreditState.BalanceReduction
            || preview.LoyaltyState != OrderAmendmentLoyaltyState.None
            || preview.RefundState != OrderAmendmentRefundState.None)
            throw new ConflictException("This amendment requires financial reconciliation before its credit can be applied.");

        var money = new AccountMoney(preview.Currency);
        var acceptedBilling = await context.OrderBillingSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrderId == source.Id, cancellationToken);
        if (acceptedBilling is not null && !CanApplyUnpaidCredit(acceptedBilling))
            throw new ConflictException("The source order's accepted loyalty evaluation is pending.");
        await OrderBillingCreditConsistency.AssertAsync(context, new[] { source.Id }, cancellationToken);
        await AssertCurrentAggregateAsync(source, money, cancellationToken);
        var existing = context.OrderBillingCredits.Local.SingleOrDefault(value => value.AmendmentId == amendment.Id)
            ?? await context.OrderBillingCredits.AsNoTracking()
                .SingleOrDefaultAsync(value => value.AmendmentId == amendment.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.SourceOrderId != source.Id || existing.AmountMinor != preview.PotentialCreditMinor
                || existing.Currency != money.Currency || existing.ActorUserId != actorId
                || existing.ActorRole != amendment.ActorRole)
                throw new ConflictException("The original billing credit has different frozen evidence.");
            return;
        }

        if (source.ExternalReference is not null || source.TotalPaid != 0 || source.Tax != 0
            || source.FidelityPointsEarned != 0 || source.FidelityPointsRedeemed != 0 || source.FidelityPointsDiscount != 0
            || source.Payments.Any(value => value.Status.IsCaptured() || value.Status == PaymentStatus.Processing
                || value.IsRefunded || value.RefundedAmount.HasValue))
            throw new ConflictException("A paid, processing, or marketplace order requires credit reconciliation.");
        var credits = money.ToMinor(source.BillingCreditAmount);
        var foodCharge = FrozenOrderChargeMath.Read(source, money).FoodMinor;
        var nextCredit = checked(credits + preview.PotentialCreditMinor);
        if (nextCredit > foodCharge)
            throw new ConflictException("The billing credit exceeds the frozen food charge.");

        var audit = currentUser.GetAuditIdentifier();
        context.OrderBillingCredits.Add(new OrderBillingCredit
        {
            Id = Guid.NewGuid(),
            SourceOrderId = source.Id,
            AmendmentId = amendment.Id,
            AmountMinor = preview.PotentialCreditMinor,
            Currency = money.Currency,
            ActorUserId = actorId,
            ActorRole = amendment.ActorRole,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = audit
        });
        source.BillingCreditAmount = money.ToMajor(nextCredit);
        source.RemainingAmount = source.PayableTotal;
        source.PaymentStatus = source.RemainingAmount == 0 ? PaymentStatus.Completed : PaymentStatus.Pending;
        source.UpdatedAt = DateTime.UtcNow;
        source.UpdatedBy = audit;
    }

    private async Task AssertCurrentAggregateAsync(Order source, AccountMoney money,
        CancellationToken cancellationToken)
    {
        var entries = await context.OrderBillingCredits.AsNoTracking()
            .Where(value => value.SourceOrderId == source.Id).ToListAsync(cancellationToken);
        var persistedIds = entries.Select(value => value.Id).ToHashSet();
        entries.AddRange(context.OrderBillingCredits.Local.Where(value => value.SourceOrderId == source.Id
            && context.Entry(value).State == EntityState.Added && !persistedIds.Contains(value.Id)));
        if (entries.Any(value => value.Currency != money.Currency)
            || money.ToMinor(source.BillingCreditAmount) != entries.Sum(value => value.AmountMinor))
            throw new ConflictException("The current order credit differs from its immutable billing journal.");
    }

    private static bool CanApplyUnpaidCredit(OrderBillingSnapshot acceptedBilling) =>
        acceptedBilling.EffectiveEarningDisposition switch
        {
            OrderBillingEarningDisposition.Evaluated => acceptedBilling.EarnedPointsCandidate.HasValue,
            OrderBillingEarningDisposition.NoCustomerOwnerAtAcceptance
                or OrderBillingEarningDisposition.LoyaltyModuleDisabledAtAcceptance =>
                !acceptedBilling.EarnedPointsCandidate.HasValue,
            _ => false
        };
}
