using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Copied rule evidence from the one evaluation that priced this order.</summary>
internal sealed record OrderBillingEarningRuleEvidence(
    Guid Id,
    string Name,
    decimal MinimumOrderAmount,
    decimal? MaximumOrderAmount,
    int PointsAwarded,
    int Priority);

/// <summary>Null candidate means unevaluated; zero is an evaluated no-award result.</summary>
internal sealed record OrderBillingEarningEvaluation(
    int? CandidatePoints,
    string? AlgorithmVersion,
    string? RuleSetFingerprint,
    OrderBillingEarningRuleEvidence? MatchedRule);

/// <summary>The exact persisted negative debit row returned by the redemption boundary.</summary>
internal sealed record OrderBillingRedemptionEvidence(
    Guid TransactionId,
    Guid UserId,
    Guid? OrderId,
    TransactionType TransactionType,
    int Points,
    decimal DiscountAmount,
    decimal? OrderTotal);
