using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

/// <summary>Proves every materialized credit has the matching resolved amendment and journal row.</summary>
internal static class OrderBillingCreditConsistency
{
    internal static async Task AssertAsync(ApplicationDbContext context, IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0) return;
        var orders = await context.Orders.AsNoTracking().Where(value => orderIds.Contains(value.Id))
            .Select(value => new BillingCreditOrder(value.Id, value.BillingCreditAmount)).ToListAsync(cancellationToken);
        if (orders.Count != orderIds.Distinct().Count())
            throw InvalidJournal();
        var amendments = await context.OrderAmendments.AsNoTracking()
            .Where(value => orderIds.Contains(value.SourceOrderId) && value.State == OrderAmendmentState.Committed)
            .Select(value => new BillingCreditAmendment(value.Id, value.SourceOrderId, value.ActorUserId,
                value.ActorRole, value.FinancialResolutionJson)).ToListAsync(cancellationToken);
        var credits = await context.OrderBillingCredits.AsNoTracking()
            .Where(value => orderIds.Contains(value.SourceOrderId)).ToListAsync(cancellationToken);
        Validate(orders, amendments, credits);
    }

    internal static void Validate(IReadOnlyList<BillingCreditOrder> orders,
        IReadOnlyList<BillingCreditAmendment> amendments, IReadOnlyList<OrderBillingCredit> credits)
    {
        if (credits.Select(value => value.AmendmentId).Distinct().Count() != credits.Count
            || orders.Select(value => value.Id).Distinct().Count() != orders.Count
            || amendments.Select(value => value.Id).Distinct().Count() != amendments.Count)
            throw InvalidJournal();
        var resolved = new Dictionary<Guid, (BillingCreditAmendment Amendment, OrderAmendmentFinancialPreviewDto Outcome)>();
        foreach (var amendment in amendments)
        {
            OrderAmendmentFinancialGuard.AssertResolved(amendment.FinancialResolutionJson);
            var outcome = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(amendment.FinancialResolutionJson);
            if (outcome.PotentialCreditMinor > 0)
            {
                if (outcome.CreditState is not (OrderAmendmentCreditState.BalanceReduction or OrderAmendmentCreditState.Resolved)
                    || outcome.ResolutionStatus != OrderAmendmentFinancialResolutionStatus.Resolved)
                    throw InvalidJournal();
                resolved.Add(amendment.Id, (amendment, outcome));
            }
        }
        if (resolved.Count != credits.Count)
            throw InvalidJournal();
        foreach (var credit in credits)
        {
            if (!resolved.TryGetValue(credit.AmendmentId, out var evidence)
                || credit.SourceOrderId != evidence.Amendment.SourceOrderId
                || credit.ActorUserId != evidence.Amendment.ActorUserId
                || credit.ActorRole != evidence.Amendment.ActorRole
                || credit.AmountMinor <= 0 || credit.AmountMinor != evidence.Outcome.PotentialCreditMinor
                || credit.Currency != evidence.Outcome.Currency
                || credit.Currency != new AccountMoney(credit.Currency).Currency)
                throw InvalidJournal();
        }
        foreach (var order in orders)
        {
            var entries = credits.Where(value => value.SourceOrderId == order.Id).ToArray();
            if (entries.Length == 0)
            {
                if (order.Amount != 0) throw InvalidJournal();
                continue;
            }
            var money = new AccountMoney(entries[0].Currency);
            if (entries.Any(value => value.Currency != money.Currency)
                || money.ToMinor(order.Amount) != entries.Sum(value => value.AmountMinor))
                throw InvalidJournal();
        }
        if (credits.Any(value => orders.All(order => order.Id != value.SourceOrderId)))
            throw InvalidJournal();
    }

    private static ConflictException InvalidJournal() => new(
        "The billing credit journal, amendment outcome, and order balance require reconciliation.");
}

internal sealed record BillingCreditOrder(Guid Id, decimal Amount);
internal sealed record BillingCreditAmendment(
    Guid Id, Guid SourceOrderId, Guid ActorUserId, string ActorRole, string FinancialResolutionJson);
