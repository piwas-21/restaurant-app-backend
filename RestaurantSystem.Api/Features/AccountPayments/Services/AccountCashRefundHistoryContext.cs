using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal sealed record AccountCashHistoryEvidence(
    OrderAmendmentRefundLeg[] Legs,
    AccountCashRefundIntent[] Intents,
    AccountPaymentAllocationReversal[] Reversals,
    IReadOnlyList<OrderAmendmentRefundEvidence> Evidence,
    IReadOnlyList<OrderAmendmentResolutionOperation> Operations);

internal sealed record AccountCashRefundProof(
    OrderAmendmentRefundLeg Leg,
    OrderAmendmentResolutionOperation Operation,
    AccountCashCollectionReceipt Receipt,
    List<OrderAmendmentRefundScope> Scopes,
    OrderAmendmentRefundEvidence[] Evidence,
    AccountPaymentAllocationReversal[] Reversals,
    Dictionary<Guid, AccountPaymentAllocation> Allocations);
