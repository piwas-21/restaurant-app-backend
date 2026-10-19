using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public partial record OrderDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsTableBill { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal CashierBillRefundedAmount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal CashierBillCredit { get; set; }
}
