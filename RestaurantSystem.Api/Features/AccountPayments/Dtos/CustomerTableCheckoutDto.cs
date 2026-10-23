namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

public sealed record CustomerTableCheckoutDto(int SchemaVersion, Guid TenantId, Guid OperationId,
    Guid ServiceSessionId, Guid? AttemptId, string Outcome, long AmountMinor, string Currency,
    DateTime? ExpiresAt, string? CheckoutUrl);
