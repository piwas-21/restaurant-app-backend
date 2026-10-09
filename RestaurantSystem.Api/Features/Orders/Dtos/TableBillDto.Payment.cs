namespace RestaurantSystem.Api.Features.Orders.Dtos;

public partial record TableBillDto
{
    /// <summary>Gratuities captured with table tenders, outside the order debt balance.</summary>
    public decimal PaymentTip { get; set; }

    /// <summary>Most recently selected table-payment flow for the printed bill.</summary>
    public string? PaymentFlowMode { get; set; }

    public int? GuestCount { get; set; }
    public List<TableBillGuestAmountDto> GuestAmounts { get; set; } = [];
}
