using RestaurantSystem.Api.Features.Orders.Dtos;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Dtos;

/// <summary>Authenticated reconciliation result for one session payment operation.</summary>
public record TableServiceSessionPaymentOperationLookupDto
{
    public Guid OperationId { get; init; }
    public string Status { get; init; } = string.Empty;
    public TableServiceSessionDto? Session { get; init; }
    public IReadOnlyList<OrderPaymentDto> Payments { get; init; } = Array.Empty<OrderPaymentDto>();
}
