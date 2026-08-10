using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderPricingService : IOrderPricingService
{
    private readonly ApplicationDbContext _context;
    private readonly ICustomerDiscountService _customerDiscountService;
    private readonly OrderSettings _orderSettings;
    private readonly ILogger<OrderPricingService> _logger;

    public OrderPricingService(
        ApplicationDbContext context,
        ICustomerDiscountService customerDiscountService,
        IOptions<OrderSettings> orderSettings,
        ILogger<OrderPricingService> logger)
    {
        _context = context;
        _customerDiscountService = customerDiscountService;
        _orderSettings = orderSettings.Value;
        _logger = logger;
    }

    public async Task ApplyAsync(
        Order order,
        decimal itemsTotal,
        CreateOrderCommand command,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        // TAX IS DELIBERATELY LEFT AT 0 — do not "fix" this by calling ITaxConfigurationService here
        // without first reading §6 of SOFRA-PAYMENTS-PLAN.
        //
        // Tax was 0 on every real order before S0b (the basket path won, and basket.Tax has three
        // write sites, all literal 0), so the service's arithmetic was dead code. It is also wrong
        // twice over, which is why switching it on is its own slice and not a side effect of a
        // security fix:
        //   1. UNIT. TaxConfiguration.Rate is documented and seeded as a FRACTION (0.08 for 8% —
        //      Entities/TaxConfiguration.cs:8, TaxConfigurationSeeder.cs:19) while
        //      TaxConfigurationService.cs:121 divides by 100 again. A seeded box yields 0.08% tax.
        //   2. DIRECTION. `amount * Rate / 100` adds tax ON TOP. Swiss VAT is price-INCLUSIVE, so
        //      extracting it needs `amount * Rate / (100 + Rate)`.
        // Resolving either needs the live `tax_configurations` rows, which cannot be read from here.
        // Publishing a wrong VAT figure into the Z-report is worse than the honest zero it replaces.
        order.SubTotal = itemsTotal;
        order.DeliveryFee = command.Type == OrderType.Delivery ? _orderSettings.DeliveryFee : 0;

        await ApplyUserLimitDiscountAsync(order, command, userId, itemsTotal, cancellationToken);
        await ApplyCustomerDiscountAsync(order, userId, itemsTotal, cancellationToken);

        RecalculateTotal(order);
    }

    private async Task ApplyUserLimitDiscountAsync(
        Order order, CreateOrderCommand command, Guid? userId, decimal itemsTotal, CancellationToken ct)
    {
        if (!command.HasUserLimitDiscount || itemsTotal < command.UserLimitAmount)
        {
            return;
        }

        // Guest/anonymous orders have no userId — there's no user-limit
        // discount to apply. Mirrors ApplyCustomerDiscountAsync's null guard
        // and avoids passing a null key into FindAsync (which throws
        // ArgumentNullException on the keyValues-array overload).
        if (!userId.HasValue)
        {
            return;
        }

        // FindAsync checks the change tracker first for a primary-key match
        // regardless of overload, so passing the CancellationToken alongside
        // the key array preserves the original lookup semantics while letting
        // callers cancel.
        var user = await _context.Users.FindAsync(new object?[] { userId.Value }, ct);
        if (user == null || !user.IsDiscountActive)
        {
            return;
        }

        order.DiscountPercentage = user.DiscountPercentage;
        // Discount applies to items total (before tax extraction).
        order.Discount = itemsTotal * (user.DiscountPercentage / 100);
    }

    private async Task ApplyCustomerDiscountAsync(
        Order order, Guid? userId, decimal itemsTotal, CancellationToken ct)
    {
        if (!userId.HasValue)
        {
            return;
        }

        var customerDiscount = await _customerDiscountService.FindBestApplicableDiscountAsync(
            userId.Value, itemsTotal, ct);
        if (customerDiscount == null)
        {
            return;
        }

        // Discount calculated on items total (before tax extraction).
        var discountAmount = _customerDiscountService.CalculateDiscountAmount(customerDiscount, itemsTotal);
        order.CustomerDiscountAmount = discountAmount;

        // Group discounts use temporary IDs that don't exist in
        // CustomerDiscountRules — only tag the FK and increment usage for
        // individual-customer discounts.
        var isIndividualDiscount = await _context.CustomerDiscountRules
            .AnyAsync(d => d.Id == customerDiscount.Id, ct);

        if (isIndividualDiscount)
        {
            order.CustomerDiscountRuleId = customerDiscount.Id;
            await _customerDiscountService.ApplyDiscountAsync(customerDiscount.Id, ct);
        }

        _logger.LogInformation(
            "Applied customer discount {DiscountName} of ${Amount} to order",
            customerDiscount.Name, discountAmount);
    }

    public void RecalculateTotal(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        // `SubTotal + Tax` recovers the gross item money whichever way tax is handled: Tax is 0
        // today (see ApplyAsync) and SubTotal is the full items total, and if extraction is ever
        // switched on SubTotal becomes `itemsTotal − Tax` and the sum still holds. Deriving it back
        // rather than taking it as a parameter is what lets this run again after redemption, when
        // the caller no longer holds itemsTotal.
        var itemsTotal = order.SubTotal + order.Tax;

        // Tax is NOT added on top — it is an extraction for reporting, not a charge.
        var sale = itemsTotal + order.DeliveryFee - order.Discount - order.CustomerDiscountAmount;
        var hasActiveDiscount = PriceRoundingUtility.HasActiveDiscount(order.CustomerDiscountAmount + order.Discount);

        // Round the SALE, then apply the points credit, then add the tip.
        //
        // Order matters twice. The whole-franc courtesy rounding is a discount on the food, so it
        // must not swallow a points credit the customer already saw deducted, and it must never
        // reshape a tip — the customer chose that number. This also reproduces exactly what the
        // checkout page displays, so the amount charged equals the amount shown.
        var roundedSale = PriceRoundingUtility.ApplySpecialRounding(sale, hasActiveDiscount);

        // Clamped at zero: a points balance worth more than the basket must not mint a negative
        // total, which UpdatePaymentSummary would read as Overpaid.
        var payableForFood = Math.Max(0m, roundedSale - order.FidelityPointsDiscount);

        // The tip is floored too, and NOT only because the validator already refuses a negative one.
        // This is the last term added after the clamp above, so a negative tip would subtract from
        // an already-clamped total — `tip: -12.99` on a 12.99 order yields Total 0, which
        // UpdatePaymentSummary reads as Completed and the fidelity coordinator rewards. Two
        // independent guards, because the validator is a pipeline behaviour a future caller could
        // bypass while this method is the single place Total is decided.
        order.Total = payableForFood + Math.Max(0m, order.Tip);
    }
}
