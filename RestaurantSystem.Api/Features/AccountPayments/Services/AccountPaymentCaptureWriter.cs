using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Records exact manual money and allocation links atomically; no gateway call or tolerance.</summary>
public sealed class AccountPaymentCaptureWriter(
    ApplicationDbContext context, ICurrentUserService currentUser, TimeProvider timeProvider)
    : IAccountPaymentCaptureWriter
{
    public async Task RecordManualAsync(AccountPaymentAttempt attempt, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null || attempt.State != AccountPaymentState.Reserved
            || attempt.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.CreditCard))
            throw new ConflictException("Manual collection requires a reserved cash or card contribution and a locked transaction.");
        var account = await new AccountDebtSnapshotReader(context)
            .ReadAsync(attempt.ServiceSessionId, cancellationToken);
        if (account.Session.Status != TableServiceSessionStatus.Open)
            throw new ConflictException("A closed visit cannot accept manual collection.");
        var scopes = attempt.Allocations.Select(value => new AccountDebtSegment(
            value.OrderId, value.OrderItemId, value.StartOrdinal, value.UnitCount, value.MinorPerUnit)).ToArray();
        if (attempt.Currency != account.Money.Currency || attempt.AmountMinor <= 0
            || scopes.Length == 0 || AccountDebtMath.Total(scopes) != attempt.AmountMinor
            || attempt.Allocations.Any(value => value.OrderPaymentId.HasValue
                || value.AmountMinor != checked(value.MinorPerUnit * value.UnitCount)))
            throw new ConflictException("The reviewed contribution requires reconciliation before collection.");
        AccountDebtMath.Subtract(account.Debt.Outstanding, scopes);
        var orderIds = scopes.Select(value => value.OrderId).Distinct().ToArray();
        var orders = await context.Orders.Where(value => orderIds.Contains(value.Id))
            .Include(value => value.Payments).ToListAsync(cancellationToken);
        if (orders.Count != orderIds.Length || orders.Any(value => value.ServiceSessionId != attempt.ServiceSessionId))
            throw new ConflictException("The reviewed payment scope no longer belongs to this visit.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var audit = currentUser.GetAuditIdentifier();
        foreach (var order in orders)
        {
            var allocations = attempt.Allocations.Where(value => value.OrderId == order.Id).ToArray();
            var amount = account.Money.ToMajor(allocations.Sum(value => value.AmountMinor));
            foreach (var placeholder in order.Payments.Where(value => value.Status == PaymentStatus.Pending).ToArray())
            {
                order.Payments.Remove(placeholder);
                context.OrderPayments.Remove(placeholder);
            }
            var payment = new OrderPayment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Amount = amount,
                PaymentMethod = attempt.PaymentMethod,
                Status = PaymentStatus.Completed,
                Currency = account.Money.Currency,
                PaymentDate = now,
                CreatedAt = now,
                CreatedBy = audit
            };
            order.Payments.Add(payment);
            context.OrderPayments.Add(payment);
            foreach (var allocation in allocations)
            {
                allocation.OrderPaymentId = payment.Id;
                allocation.OrderPayment = payment;
            }
            var paidMinor = order.Payments.Where(value => value.Status.IsCaptured())
                .DistinctBy(value => value.Id).Sum(value => account.Money.ToMinor(value.Amount));
            var totalMinor = account.Money.ToMinor(order.Total);
            if (paidMinor > totalMinor)
                throw new ConflictException("Collection exceeds the frozen order charge.");
            order.TotalPaid = account.Money.ToMajor(paidMinor);
            order.RemainingAmount = account.Money.ToMajor(totalMinor - paidMinor);
            order.PaymentStatus = paidMinor == totalMinor ? PaymentStatus.Completed : PaymentStatus.PartiallyPaid;
            order.UpdatedAt = now;
            order.UpdatedBy = audit;
        }
        attempt.State = AccountPaymentState.Captured;
        attempt.Version++;
        attempt.CompletedAt = now;
        attempt.UpdatedAt = now;
        attempt.UpdatedBy = audit;
    }
}
