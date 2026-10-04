using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal sealed record OrderAmendmentResolutionPlanningState(
    Order Source,
    OrderAmendment Amendment,
    IReadOnlyList<OrderAmendmentChangeSnapshot> Changes,
    IReadOnlyList<AccountPaymentAttempt> Attempts,
    IReadOnlyList<AccountCheckoutJournal> Journals,
    IReadOnlyList<AccountPaymentAllocationReversal> Reversals,
    OrderAmendmentResolutionPlan Plan,
    string SourceFinancialFingerprint);
