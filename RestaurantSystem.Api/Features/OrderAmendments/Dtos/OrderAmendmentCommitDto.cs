using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentCommitDto
{
    public Guid AmendmentId { get; init; }
    public Guid ClientOperationId { get; init; }
    public Guid SourceOrderId { get; init; }
    public Guid? SupplementOrderId { get; init; }
    public long? CommittedAccountRevision { get; init; }
    public DateTime CommittedAt { get; init; }
    public required OrderAmendmentFinancialPreviewDto FinancialResolution { get; init; }
    public OrderDto? SupplementOrder { get; init; }
}
