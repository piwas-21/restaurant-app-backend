using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Tenant.Dtos;
using RestaurantSystem.Api.Features.Tenant.Queries.GetCustomerMobileBootstrapQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCancellation;

/// <summary>Read-only clearance under the caller's order lock; no money or provider operation.</summary>
public sealed class CustomerOrderCancellationRules(ApplicationDbContext context, CustomMediator mediator)
    : ICustomerOrderCancellationRules
{
    public async Task<CustomerOrderCancellationReason?> RefusalUnderLockAsync(Order order,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Cancellation clearance requires a locked transaction.");
        CustomerMobileBootstrapDto? bootstrap;
        try { bootstrap = (await mediator.SendQuery(new GetCustomerMobileBootstrapQuery(1), cancellationToken)).Data; }
        catch (NotFoundException) { return CustomerOrderCancellationReason.OrderingUnavailable; }
        if (bootstrap?.Capabilities.GetValueOrDefault("ordering") != "enabled")
            return CustomerOrderCancellationReason.OrderingUnavailable;
        if (order.Version <= 0 || order.Version == int.MaxValue)
            return CustomerOrderCancellationReason.OrderChanged;
        var configuration = await context.OrderTypeConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(value => value.OrderType == order.Type, cancellationToken);
        if (configuration is null || !configuration.IsEnabled)
            return CustomerOrderCancellationReason.OrderingUnavailable;
        var flow = configuration.ConfirmationFlow;
        var local = await IsLocalStandaloneAsync(order, cancellationToken);
        var canReviewFinancials = CustomerOrderCancellationPolicy.CanDeclineDelay(order.Status, flow, local, true);
        var financial = canReviewFinancials && await FinancialClearAsync(order, cancellationToken);
        if (CustomerOrderCancellationPolicy.CanDeclineDelay(order.Status, flow, local, financial)) return null;
        if (!local) return CustomerOrderCancellationReason.RestaurantManaged;
        return canReviewFinancials ? CustomerOrderCancellationReason.FinancialReview
            : CustomerOrderCancellationReason.NotAwaitingCustomerApproval;
    }

    private async Task<bool> IsLocalStandaloneAsync(Order order, CancellationToken cancellationToken) =>
        order.ServiceSessionId is null && (order.Type != OrderType.DineIn || order.TableNumber is null)
            && !await context.Set<ExternalOrderReference>().AnyAsync(value => value.OrderId == order.Id, cancellationToken)
            && !await context.OrderStatusHistories.AnyAsync(value => value.OrderId == order.Id
                && ((value.ToStatus != OrderStatus.Pending && value.ToStatus != OrderStatus.PendingApproval)
                    || (value.FromStatus != OrderStatus.Pending && value.FromStatus != OrderStatus.PendingApproval)), cancellationToken);

    private async Task<bool> FinancialClearAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.TotalPaid != 0 || order.PaymentStatus != PaymentStatus.Pending
            || order.CustomerDiscountRuleId is not null
            || order.BillingCreditAmount != 0 || order.BillChargeAmount != 0
            || !string.Equals(order.BillChargesJson, "[]", StringComparison.Ordinal)) return false;
        return !await HasPaymentEvidenceAsync(order, cancellationToken)
            && !await HasLoyaltyEvidenceAsync(order, cancellationToken)
            && !await HasAdjustmentEvidenceAsync(order.Id, cancellationToken);
    }

    private async Task<bool> HasPaymentEvidenceAsync(Order order, CancellationToken cancellationToken) =>
        await HasUnexpectedTenderAsync(order, cancellationToken)
        || await context.OrderCheckoutSessions.AnyAsync(value => value.OrderId == order.Id, cancellationToken)
        || await context.Set<CustomerOrderCheckoutJournal>().AnyAsync(value => value.OrderId == order.Id, cancellationToken)
        || await context.AccountPaymentAllocations.AnyAsync(value => value.OrderId == order.Id, cancellationToken);

    private async Task<bool> HasUnexpectedTenderAsync(Order order, CancellationToken cancellationToken)
    {
        // Native cash/card checkout records one uncollected restaurant intent. It is not
        // captured money; preserve it unchanged, as with the existing delay-rejection flow.
        // Any extra or enriched tender is evidence this narrow customer action cannot clear.
        var tenders = await context.OrderPayments.AsNoTracking().Where(value => value.OrderId == order.Id)
            .Take(2).ToListAsync(cancellationToken);
        return tenders.Count > 1 || tenders.Any(value => value.Status != PaymentStatus.Pending
            || (value.PaymentMethod != PaymentMethod.Cash
                && (value.PaymentMethod != PaymentMethod.CreditCard || order.Type != OrderType.Takeaway))
            || value.Amount != order.PayableTotal || value.TipMinor != 0 || value.IsRefunded
            || value.RefundedAmount is not null || value.RefundedTipMinor != 0 || value.RefundDate is not null
            || value.RefundReason is not null || value.TransactionId is not null || value.Currency is not null
            || value.ReferenceNumber is not null || value.CardLastFourDigits is not null || value.CardType is not null
            || value.PaymentGateway is not null || value.PaymentNotes is not null || value.OperationId is not null || value.TableBillPaymentOperationId is not null);
    }

    private async Task<bool> HasLoyaltyEvidenceAsync(Order order, CancellationToken cancellationToken) =>
        order.FidelityPointsRedeemed != 0 || order.FidelityPointsEarned != 0 || order.FidelityPointsDiscount != 0
        || await context.FidelityPointsTransactions.AnyAsync(value => value.OrderId == order.Id, cancellationToken)
        || await context.Set<OrderBillingAwardWitness>().AnyAsync(value => value.OrderId == order.Id, cancellationToken)
        || await context.OrderBillingSnapshots.AnyAsync(value => value.OrderId == order.Id
            && (value.RedeemedPoints != 0 || value.RedemptionDiscountMinor != 0 || value.RedemptionTransactionId != null
                || value.RedemptionTransactionType != null || value.RedemptionTransactionPoints != null
                || value.RedemptionTransactionOrderTotal != null || value.RedemptionTransactionCreatedAt != null), cancellationToken);

    private async Task<bool> HasAdjustmentEvidenceAsync(Guid orderId, CancellationToken cancellationToken) =>
        await context.OrderAmendments.AnyAsync(value => value.SourceOrderId == orderId, cancellationToken)
        || await context.Set<OrderBillingCredit>().AnyAsync(value => value.SourceOrderId == orderId, cancellationToken);
}
