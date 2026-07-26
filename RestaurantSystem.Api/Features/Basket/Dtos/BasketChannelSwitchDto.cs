using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Dtos;

/// <summary>One basket line that the requested order type does not permit.</summary>
public record BasketChannelConflictDto
{
    public Guid BasketItemId { get; init; }
    public Guid? ProductId { get; init; }
    public required string ProductName { get; init; }
    public int Quantity { get; init; }

    /// <summary>Order types this line IS available on — drives "…is takeaway &amp; delivery only".</summary>
    public IReadOnlyList<OrderType> AllowedOrderTypes { get; init; } = [];
}

/// <summary>
/// Result of setting a basket's order type. Two-phase by design: the first call reports conflicts
/// WITHOUT changing anything so the client can show an itemized confirm ("Switching to Dine-in
/// removes: Dürüm ×1"); the client then repeats the call with <c>removeConflicts</c>.
/// </summary>
public record BasketChannelSwitchDto
{
    /// <summary>False when conflicts blocked the switch — nothing was changed.</summary>
    public bool Applied { get; init; }

    /// <summary>Lines the requested order type forbids. Empty when the switch applied cleanly.</summary>
    public IReadOnlyList<BasketChannelConflictDto> Conflicts { get; init; } = [];

    /// <summary>Lines actually removed. Only non-empty when the caller opted into removal.</summary>
    public IReadOnlyList<BasketChannelConflictDto> Removed { get; init; } = [];

    /// <summary>The basket after the switch, or as-is when it was blocked.</summary>
    public BasketDto? Basket { get; init; }
}
