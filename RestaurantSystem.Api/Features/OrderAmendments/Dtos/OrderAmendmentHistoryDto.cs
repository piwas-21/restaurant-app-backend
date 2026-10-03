using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentHistoryDto
{
    public Guid AmendmentId { get; init; }
    public Guid SourceOrderId { get; init; }
    public Guid? ServiceSessionId { get; init; }
    public Guid? SupplementOrderId { get; init; }
    public string ActorRole { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? CommittedAt { get; init; }
    public OrderDto? SupplementOrder { get; init; }
    public IReadOnlyList<OrderAmendmentChangeSnapshot> Changes { get; init; } = [];
    public required OrderAmendmentFinancialPreviewDto FinancialResolution { get; init; }
}
