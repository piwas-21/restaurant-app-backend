using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentResolutionPlanningInput(
    Order Source,
    OrderAmendment Amendment,
    OrderAmendmentResolutionQuoteRequest Request,
    IReadOnlyList<OrderAmendmentChangeSnapshot> Changes,
    IReadOnlyList<AccountPaymentAttempt> Attempts,
    IReadOnlyList<AccountCheckoutJournal> CheckoutJournals,
    IReadOnlyList<AccountPaymentAllocationReversal> Reversals,
    IReadOnlyDictionary<Guid, long> PriorAuthorizedRefundMinorByPayment,
    AccountMoney Money,
    bool HasLoyaltyLedgerHistory);
