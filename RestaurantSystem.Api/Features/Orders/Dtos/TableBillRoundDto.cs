namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Settlement projection for one ordering round in a table bill.</summary>
public record TableBillRoundDto
{
    public OrderDto Order { get; init; } = new();
    public string SettlementState { get; init; } = string.Empty;
    public decimal Outstanding { get; init; }
    public decimal RefundedAmount { get; init; }
    public decimal Credit { get; init; }
    public bool CanCollect { get; init; }
    public IReadOnlyList<OrderPermittedActionDto> PermittedActions { get; init; } =
        Array.Empty<OrderPermittedActionDto>();
}
