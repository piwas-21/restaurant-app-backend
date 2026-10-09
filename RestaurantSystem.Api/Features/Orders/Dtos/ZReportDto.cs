namespace RestaurantSystem.Api.Features.Orders.Dtos;

public record ZReportDto
{
    public DateTime ReportDate { get; init; }
    public DateTime GeneratedAt { get; init; }

    // Totals
    public int TotalTransactions { get; init; }
    public decimal GrossSales { get; init; }
    public decimal NetSales { get; init; }
    public decimal TotalBillingCredits { get; init; }
    public decimal TotalTax { get; init; }
    /// <summary>Legacy guest/order-level tip total; staff tender gratuities are reported separately below.</summary>
    public decimal TotalTips { get; init; }
    public decimal TotalDeliveryFees { get; init; }
    /// <summary>Cashier/server gratuities collected with captured tenders, grouped by currency.</summary>
    public List<ZReportCurrencyAmountDto> StaffTipsCollected { get; init; } = new();
    /// <summary>Cashier/server gratuities explicitly refunded during the report window.</summary>
    public List<ZReportCurrencyAmountDto> StaffTipsRefunded { get; init; } = new();
    /// <summary>Net cash tender movement for the window; excludes opening float and physical cash counts.</summary>
    public List<ZReportCurrencyAmountDto> NetCashCollected { get; init; } = new();

    // Discounts
    public ZReportDiscountsDto Discounts { get; init; } = new();

    // Refunds
    public ZReportRefundsDto Refunds { get; init; } = new();

    // Table-account cash movement; explicitly excludes the rest of the restaurant till.
    public ZReportAccountCashMovementDto? AccountCashMovements { get; init; }

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
    public string? Currency { get; init; }
    public int TransactionCount { get; init; }
    /// <summary>Gross order settlement amount, which can include guest tip, tax and fees.</summary>
    public decimal OrderAmount { get; init; }
    /// <summary>Additional staff gratuity captured with these tenders.</summary>
    public decimal TipAmount { get; init; }
    /// <summary>Gross order settlement plus additional staff gratuity, before refunds.</summary>
    public decimal TotalAmount { get; init; }
}

public record ZReportCurrencyAmountDto
{
    /// <summary>ISO currency code, or null when old tender data has no declared currency.</summary>
    public string? Currency { get; init; }
    /// <summary>Exact minor-unit total. Net cash movements may be negative on a refund-only day.</summary>
    public long AmountMinor { get; init; }
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
