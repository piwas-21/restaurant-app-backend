using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public partial record OrderSummaryDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PayableTotal { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal BillingCreditAmount { get; set; }
}
