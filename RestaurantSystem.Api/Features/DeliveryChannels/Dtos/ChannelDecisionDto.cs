namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

/// <summary>Staff-visible delivery state; contains no provider resource identifiers or lease secrets.</summary>
public sealed record ChannelDecisionDto(Guid OperationId, Guid OrderId, string Action, string State,
    DateTime CreatedAt, DateTime? LastObservedAt);
