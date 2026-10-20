namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Payment-attempt state is separate from order acceptance/fulfillment and current receipt balance.</summary>
public sealed record CustomerOrderPaymentDto(int SchemaVersion, Guid OperationId, Guid OrderId,
    string Outcome, string? CheckoutUrl, DateTime ExpiresAt);
