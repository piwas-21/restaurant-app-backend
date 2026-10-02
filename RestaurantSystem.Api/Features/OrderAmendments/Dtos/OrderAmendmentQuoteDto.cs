using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentQuoteDto
{
    public Guid AmendmentId { get; init; }
    public Guid SourceOrderId { get; init; }
    public Guid? ServiceSessionId { get; init; }
    public int ExpectedOrderVersion { get; init; }
    public long? ExpectedAccountRevision { get; init; }
    public DateTime ExpiresAt { get; init; }
    public required OrderDto SourceOrder { get; init; }
    public OrderDto? SupplementOrder { get; init; }
    public IReadOnlyList<OrderAmendmentChangeSnapshot> Changes { get; init; } = [];
    public required OrderAmendmentFinancialPreviewDto FinancialPreview { get; init; }
    public string? ProviderProcedure { get; init; }
}
