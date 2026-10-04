using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentFinancialSourceState(
    Order Source,
    OrderAmendment Amendment,
    IReadOnlyList<OrderAmendment> SourceAmendments,
    IReadOnlyList<AccountPaymentAttempt> Attempts,
    IReadOnlyList<AccountCheckoutJournal> Journals,
    IReadOnlyList<AccountPaymentAllocationReversal> Reversals,
    IReadOnlyDictionary<Guid, long> AuthorizedRefundMinorByPayment,
    IReadOnlyList<OrderBillingCredit> Credits,
    IReadOnlyList<FidelityPointsTransaction> LoyaltyTransactions,
    string Currency);
