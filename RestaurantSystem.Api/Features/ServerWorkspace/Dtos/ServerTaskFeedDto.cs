using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.ServerWorkspace.Dtos;

/// <summary>One server-owned page of operational service tasks.</summary>
public sealed record ServerTaskFeedDto
{
    public DateTime ServerTime { get; init; }
    public IReadOnlyList<ServerServiceTaskDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public string? NextCursor { get; init; }
    public bool HasMore { get; init; }
    public IReadOnlyList<Guid> RemovedOrderIds { get; init; } = [];
}

/// <summary>One actionable order with server-computed urgency and delivery permissions.</summary>
public sealed record ServerServiceTaskDto
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public OrderType OrderType { get; init; }
    public OrderStatus Status { get; init; }
    public string Bucket { get; init; } = string.Empty;
    public DateTime ActionableAt { get; init; }
    public long AgeSeconds { get; init; }
    public Guid? TableId { get; init; }
    public string? TableLabel { get; init; }
    public int? TableNumber { get; init; }
    public Guid? ServiceSessionId { get; init; }
    public decimal Total { get; init; }
    public decimal RemainingAmount { get; init; }
    public int Version { get; init; }
    public string RoutingState { get; init; } = string.Empty;
    public bool HasRequiredRoutingException { get; init; }
    public bool HasOptionalRoutingException { get; init; }
    public IReadOnlyList<OrderRoutingStateDto> Routing { get; init; } = [];
    public IReadOnlyList<ServerTaskActionDto> PermittedDeliveryActions { get; init; } = [];
}

/// <summary>One server-computed delivery action and refusal reason.</summary>
public sealed record ServerTaskActionDto(
    string Action,
    bool Allowed,
    string? ReasonCode,
    OrderStatus? TargetStatus);
