namespace RestaurantSystem.Api.Features.DeliveryChannels.Dtos;

/// <summary>Machine-only delivery envelope. Tenant identity comes from authenticated endpoint configuration.</summary>
public sealed record ChannelDecisionLeaseDto(Guid DecisionId, Guid LeaseId, DateTime LeaseUntil,
    string Provider, string StoreId, string ExternalOrderId, string Action, string Reason, int Attempt, Guid OrderId);
