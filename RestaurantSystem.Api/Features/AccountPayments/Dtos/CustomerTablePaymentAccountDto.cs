namespace RestaurantSystem.Api.Features.AccountPayments.Dtos;

/// <summary>Current participant account only; this snapshot does not authorize a payment.</summary>
public sealed record CustomerTablePaymentAccountDto(
    int SchemaVersion, Guid TenantId, AccountPaymentAccountDto Account);
