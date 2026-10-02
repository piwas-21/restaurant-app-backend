namespace RestaurantSystem.Api.Features.DeliveryChannels.Management.Dtos;

public sealed record DeliveryChannelOAuthStartDto(
    Guid FlowId,
    string AuthorizationUrl,
    DateTimeOffset ExpiresAt);

public sealed record DeliveryChannelOAuthStartRequest(bool EnableOrderAcceptance = false);

public sealed record DeliveryChannelOAuthFlowDto(
    Guid FlowId,
    string Status,
    Guid StoreId,
    bool StoreConfirmed,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    string? ErrorCode);
