namespace RestaurantSystem.Api.Features.FidelityPoints.Dtos;

/// <summary>Recorded points only. Spendability and discounts are recalculated at checkout.</summary>
public sealed record CustomerLoyaltyBalanceDto(int SchemaVersion, int CurrentPoints,
    int TotalEarnedPoints, int TotalRedeemedPoints, DateTime? LastUpdated);
