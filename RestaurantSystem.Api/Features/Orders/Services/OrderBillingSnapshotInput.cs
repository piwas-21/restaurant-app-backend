using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Copied rule evidence from the one evaluation that priced this order.</summary>
public sealed record OrderBillingEarningRuleEvidence(
    Guid Id,
    string Name,
    decimal MinimumOrderAmount,
    decimal? MaximumOrderAmount,
    int PointsAwarded,
    int Priority);

/// <summary>Null candidate means unevaluated; zero is an evaluated no-award result.</summary>
public sealed record OrderBillingEarningEvaluation(
    int? CandidatePoints,
    string? AlgorithmVersion,
    string? RuleSetFingerprint,
    OrderBillingEarningRuleEvidence? MatchedRule,
    OrderBillingEarningDisposition? Disposition = null);

/// <summary>The exact persisted negative debit row returned by the redemption boundary.</summary>
public sealed record OrderBillingRedemptionEvidence(
    Guid TransactionId,
    Guid UserId,
    Guid? OrderId,
    TransactionType TransactionType,
    int Points,
    decimal DiscountAmount,
    decimal? OrderTotal,
    DateTime CreatedAt);
