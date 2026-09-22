namespace RestaurantSystem.Api.Features.TableServiceSessions.Dtos;

/// <summary>Server-authoritative state for one cashier collection request.</summary>
public sealed record TableServicePaymentHandoffDto
{
    public Guid HandoffId { get; init; }
    public Guid ServiceSessionId { get; init; }
    public Guid OperationId { get; init; }
    public Guid? TableId { get; init; }
    public int? TableNumber { get; init; }
    public string TableLabel { get; init; } = string.Empty;
    public int ExpectedVersion { get; init; }
    public decimal RequestedAmount { get; init; }
    public string? RequestedCurrency { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public Guid? ResolvedPaymentOperationId { get; init; }
    public DateTime? CancelledAt { get; init; }
}
