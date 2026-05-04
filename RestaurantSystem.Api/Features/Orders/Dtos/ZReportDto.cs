namespace RestaurantSystem.Api.Features.Orders.Dtos;

public record ZReportDto
{
    public DateTime ReportDate { get; init; }
    public DateTime GeneratedAt { get; init; }

    // Totals
    public int TotalTransactions { get; init; }
    public decimal GrossSales { get; init; }
    public decimal NetSales { get; init; }
    public decimal TotalTax { get; init; }
    public decimal TotalTips { get; init; }
    public decimal TotalDeliveryFees { get; init; }

    // Discounts
    public ZReportDiscountsDto Discounts { get; init; } = new();

    // Refunds
    public ZReportRefundsDto Refunds { get; init; } = new();

    // Cancellations
    public int CancelledOrdersCount { get; init; }
    public decimal CancelledOrdersTotal { get; init; }

    // Breakdowns
    public List<ZReportPaymentMethodDto> PaymentsByMethod { get; init; } = new();
    public List<ZReportOrderTypeDto> SalesByOrderType { get; init; } = new();
    public List<ZReportProductTypeDto> SalesByProductType { get; init; } = new();
    public List<ZReportTopItemDto> TopSellingItems { get; init; } = new();
}

public record ZReportDiscountsDto
{
    public decimal TotalDiscounts { get; init; }
    public decimal PromoCodeDiscounts { get; init; }
    public decimal CustomerDiscounts { get; init; }
    public decimal FidelityPointsDiscounts { get; init; }
}

public record ZReportRefundsDto
{
    public int RefundCount { get; init; }
    public decimal TotalRefundedAmount { get; init; }
}

public record ZReportPaymentMethodDto
{
    public string PaymentMethod { get; init; } = null!;
    public int TransactionCount { get; init; }
    public decimal TotalAmount { get; init; }
}

public record ZReportOrderTypeDto
{
    public string OrderType { get; init; } = null!;
    public int OrderCount { get; init; }
    public decimal TotalAmount { get; init; }
}

public record ZReportProductTypeDto
{
    public string ProductType { get; init; } = null!;
    public int ItemCount { get; init; }
    public decimal TotalAmount { get; init; }
}

public record ZReportTopItemDto
{
    public string ProductName { get; init; } = null!;
    public int QuantitySold { get; init; }
    public decimal TotalRevenue { get; init; }
}
