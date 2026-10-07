namespace RestaurantSystem.Api.Features.FidelityPoints.Models;

public enum FidelityPointsAwardDisposition
{
    Awarded = 1,
    AlreadyAwarded = 2,
    EvaluatedZero = 3,
    FullySuppressed = 4,
    Deferred = 5
}

public enum FidelityPointsAwardDeferralReason
{
    OrderUnavailable = 1,
    PaymentNotSettled = 2,
    OrderCancelled = 3,
    ProviderManaged = 4,
    MissingSnapshot = 5,
    CandidateUnevaluated = 6,
    EarningOwnerUnavailable = 7
}

/// <summary>
/// A typed no-failure result for an award that is not currently eligible. Point totals remain
/// null when eligibility/evaluation is deferred, so an unknown candidate cannot be read as zero.
/// </summary>
public sealed record FidelityPointsAwardResult(
    FidelityPointsAwardDisposition Disposition,
    FidelityPointsAwardDeferralReason? DeferralReason,
    int? CandidatePoints,
    int? AppliedPoints,
    int? SuppressedPoints);
