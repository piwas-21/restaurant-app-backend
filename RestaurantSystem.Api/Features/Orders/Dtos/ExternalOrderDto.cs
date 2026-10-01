using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Operational marketplace source; never contains credentials or raw provider/customer payloads.</summary>
public sealed record ExternalOrderDto(
    string Provider,
    string ExternalDisplayId,
    string ExternalState,
    DateTime LastEventAt,
    string Currency,
    decimal MerchantTotal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] decimal? ReportedTax,
    string FulfillmentType,
    bool IsSandbox);
