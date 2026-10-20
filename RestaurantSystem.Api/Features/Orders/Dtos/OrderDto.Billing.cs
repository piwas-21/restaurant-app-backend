using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public partial record OrderDto
{
    public decimal BillChargeAmount { get; set; }
    public decimal? BillChargeItemBasis { get; set; }
    public bool HasPendingBillingCorrection { get; set; }
    public IReadOnlyList<RestaurantSystem.Api.Features.BillCharges.Dtos.BillChargeDto> BillCharges { get; set; } = [];

    /// <summary>Effective charge after amendment credits; absent on an unchanged order.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? PayableTotal { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal BillingCreditAmount { get; set; }
}
