namespace RestaurantSystem.Api.Features.Orders.Dtos;

public partial record TableBillDto
{
    /// <summary>Account-content revision for an explicit visit; null on a legacy bill.</summary>
    public long? AccountRevision { get; set; }

    /// <summary>Root order lines with immutable unit identities; empty on a legacy bill.</summary>
    public List<TableBillAccountItemDto> AccountItems { get; set; } = [];

    public List<TableAccountActivityDto> AccountActivity { get; set; } = [];
    public bool HasMoreAccountActivity { get; set; }
}

/// <summary>A root charge line from one immutable session-member order.</summary>
public record TableBillAccountItemDto
{
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public Guid OrderItemId { get; init; }
    public OrderItemDto ItemSnapshot { get; init; } = new();
    /// <summary>Stable unit ordinals are 1 through UnitCount; they are not eagerly expanded.</summary>
    public int UnitCount { get; init; }
}
