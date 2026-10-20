namespace RestaurantSystem.Api.Features.FidelityPoints.Dtos;

public sealed record CustomerLoyaltyHistoryDto(int SchemaVersion,
    IReadOnlyList<CustomerLoyaltyMovementDto> Items, CustomerLoyaltyCursorDto? Next);

public sealed record CustomerLoyaltyMovementDto(Guid TransactionId, string Kind, int Points,
    DateTime CreatedAt, DateTime? ExpiresAt);

public sealed record CustomerLoyaltyCursorDto(DateTime CreatedAt, Guid TransactionId);
