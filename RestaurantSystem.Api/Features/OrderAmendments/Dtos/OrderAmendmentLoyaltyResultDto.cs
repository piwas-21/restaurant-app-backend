using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentLoyaltyResultDto(
    OrderAmendmentLoyaltyOperationStatus State,
    bool AwardPending,
    int? CandidatePoints,
    int AppliedAwardPoints,
    int SuppressedPoints,
    int EarnedClawbackPoints,
    int RedemptionRestorationPoints,
    int PostedClawbackPoints,
    int PostedRestorationPoints,
    long? AvailablePointsBeforeClawback,
    int? ClawbackShortfallPoints,
    OrderBillingEarningDisposition EarningDisposition = OrderBillingEarningDisposition.Evaluated,
    bool EarningRetired = false);
