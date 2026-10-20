using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

/// <summary>Canonical paid/expired evidence and its tender land atomically; pending never books money.</summary>
public sealed class CustomerOrderCheckoutLedger(ApplicationDbContext context, IOrderPaymentBuilder payments,
    ICurrentUserService currentUser) : ICustomerOrderCheckoutLedger
{
    public async Task<CheckoutSessionStatus?> ApplyAsync(CustomerOrderCheckoutJournal journal,
        StripeCheckoutSession evidence, DateTime now, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Native settlement requires the evidence transaction and order lock.");
        if (!await context.Set<CustomerOrderOperation>().AsNoTracking().AnyAsync(value => value.OperationId == journal.OperationId
            && value.OrderId == journal.OrderId && !value.IsCancelled, cancellationToken)) throw Reconcile();
        var billing = await context.OrderBillingSnapshots.AsNoTracking().SingleOrDefaultAsync(value => value.OrderId == journal.OrderId,
            cancellationToken);
        if (billing is null || billing.Currency.ToLowerInvariant() != journal.Currency || billing.TotalMinor != journal.AmountMinor)
            throw Reconcile();
        var request = CustomerOrderCheckoutReplay.Read(journal, now, forCreate: false);
        NativeCheckoutSessionEvidence.Require(request, evidence);
        var terminal = Terminal(request, evidence);
        if (terminal is null) return null;
        var previous = await context.OrderCheckoutSessions.AsNoTracking()
            .Where(value => value.OrderId == journal.OrderId).Take(2).ToArrayAsync(cancellationToken);
        if (previous.Length != 0)
        {
            if (previous.Length != 1 || !Matches(previous[0], journal, evidence, terminal.Value)) throw Reconcile();
            if (terminal == CheckoutSessionStatus.Completed)
                await RequireRetainedTenderAsync(previous[0], journal, cancellationToken);
            return terminal;
        }
        var order = await context.Orders.SingleOrDefaultAsync(value => value.Id == journal.OrderId
            && !value.IsDeleted && value.ServiceSessionId == null, cancellationToken) ?? throw Reconcile();
        order.Payments = await context.OrderPayments.Where(value => value.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        var audit = currentUser.GetAuditIdentifier();
        var session = new OrderCheckoutSession
        {
            OrderId = journal.OrderId,
            SessionId = evidence.Id,
            PaymentIntentId = evidence.PaymentIntentId,
            Status = terminal.Value,
            Currency = journal.Currency,
            AmountMinor = journal.AmountMinor,
            AmountReceivedMinor = terminal == CheckoutSessionStatus.Completed ? evidence.AmountTotalMinor : null,
            IdempotencyKey = journal.CreateIdempotencyKey,
            ExpiresAt = journal.ExpiresAt,
            ConnectedAccountId = journal.ProviderAccountId,
            ReconciledAt = now,
            CreatedBy = audit
        };
        if (terminal == CheckoutSessionStatus.Completed)
        {
            var tender = OnlineTenderCompletion.Apply(order, session, evidence.PaymentIntentId,
                evidence.AmountTotalMinor, audit, now);
            if (tender.Id == Guid.Empty) tender.Id = Guid.NewGuid();
            session.OrderPaymentId = tender.Id;
        }
        else
        {
            foreach (var tender in order.Payments.Where(value => value.PaymentMethod == PaymentMethod.OnlinePayment
                && value.Status == PaymentStatus.Processing))
            {
                tender.Status = PaymentStatus.Failed;
                tender.UpdatedAt = now;
                tender.UpdatedBy = audit;
            }
        }
        payments.UpdatePaymentSummary(order);
        order.UpdatedAt = now;
        order.UpdatedBy = audit;
        context.OrderCheckoutSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
        return terminal;
    }

    private async Task RequireRetainedTenderAsync(OrderCheckoutSession row, CustomerOrderCheckoutJournal journal,
        CancellationToken cancellationToken)
    {
        var tender = await context.OrderPayments.AsNoTracking().SingleOrDefaultAsync(value => value.Id == row.OrderPaymentId
            && value.OrderId == journal.OrderId, cancellationToken);
        if (tender is null || tender.PaymentMethod != PaymentMethod.OnlinePayment || !tender.Status.IsCaptured()
            || tender.TransactionId != row.PaymentIntentId || tender.PaymentGateway != OnlineTenderCompletion.GatewayName
            || tender.Currency?.ToLowerInvariant() != journal.Currency
            || CheckoutAmount.From(tender.Amount, journal.Currency).Minor != journal.AmountMinor) throw Reconcile();
    }

    private static CheckoutSessionStatus? Terminal(CheckoutSessionRequest request, StripeCheckoutSession evidence)
    {
        var intent = evidence.NativeIntentEvidence;
        if (intent is not null) NativeCheckoutIntentEvidence.Require(request, evidence, intent);
        if ((evidence.IsComplete || evidence.IsExpired) && evidence.PaymentIntentId is not null && intent is null)
            throw Reconcile();
        if (evidence.IsComplete && evidence.IsPaid)
        {
            if (intent is null) throw Reconcile();
            return intent.IsSucceeded && intent.AmountReceivedMinor == request.AmountMinor
                ? CheckoutSessionStatus.Completed : null;
        }
        if (evidence.IsExpired && evidence.PaymentStatus == "unpaid")
            return intent is null || intent.HasFailed && intent.AmountReceivedMinor == 0 ? CheckoutSessionStatus.Expired : null;
        if (evidence.IsComplete && evidence.PaymentStatus == "unpaid" && intent is not null
            && intent.HasFailed && intent.AmountReceivedMinor == 0) return CheckoutSessionStatus.Failed;
        if (evidence.IsPaid || evidence.IsExpired) throw Reconcile();
        return null;
    }

    private static bool Matches(OrderCheckoutSession row, CustomerOrderCheckoutJournal journal,
        StripeCheckoutSession evidence, CheckoutSessionStatus terminal) => row.SessionId == evidence.Id
        && row.Currency == journal.Currency && row.AmountMinor == journal.AmountMinor
        && row.IdempotencyKey == journal.CreateIdempotencyKey && row.ExpiresAt == journal.ExpiresAt
        && row.ConnectedAccountId == journal.ProviderAccountId && row.Status == terminal
        && row.ReconciledAt is not null
        && (terminal != CheckoutSessionStatus.Completed || row.PaymentIntentId == evidence.PaymentIntentId
            && row.AmountReceivedMinor == journal.AmountMinor && row.OrderPaymentId is not null);

    private static ConflictException Reconcile() => new("The original payment needs financial reconciliation.");
}
