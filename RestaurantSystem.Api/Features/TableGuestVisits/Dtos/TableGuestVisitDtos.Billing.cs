using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

public sealed partial record TableGuestOrderDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PayableTotal { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal BillingCreditAmount { get; init; }
}

public sealed partial record TableGuestAccountDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? OriginalTotal { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal BillingCreditAmount { get; init; }
}
