using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record OrderAmendmentPrintContextDto(
    Guid AmendmentId, Guid SourceOrderId, string SourceOrderNumber);

public partial record OrderDto
{
    /// <summary>Printer-only backlink for a supplement; null for the ordinary order contract.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OrderAmendmentPrintContextDto? AmendmentPrintContext { get; set; }
}
