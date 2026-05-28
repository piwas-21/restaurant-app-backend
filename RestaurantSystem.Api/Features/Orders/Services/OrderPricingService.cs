using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Settings.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public class OrderPricingService : IOrderPricingService
{
    // Fixed delivery fee. Promote to config (e.g. OrderSettings:DeliveryFee
    // or distance-based via ITaxConfigurationService-style provider) if
    // dynamic pricing is ever needed.
    private const decimal FlatDeliveryFee = 5.00m;

    private readonly ApplicationDbContext _context;
    private readonly ICustomerDiscountService _customerDiscountService;
    private readonly ITaxConfigurationService _taxConfigurationService;
    private readonly ILogger<OrderPricingService> _logger;

    public OrderPricingService(
        ApplicationDbContext context,
        ICustomerDiscountService customerDiscountService,
        ITaxConfigurationService taxConfigurationService,
        ILogger<OrderPricingService> logger)
    {
        _context = context;
        _customerDiscountService = customerDiscountService;
        _taxConfigurationService = taxConfigurationService;
        _logger = logger;
    }

    public async Task ApplyAsync(
        Order order,
        decimal itemsTotal,
        CreateOrderCommand command,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (TryUsePreCalculatedBasketValues(order, command))
        {
            // Pre-calculated path: subtotal, tax, discounts already settled
            // on the basket side. Skip the legacy compute branch.
        }
        else
        {
            // Legacy compute path. Tax flow: tax is extracted from item
            // prices for display only — it does NOT affect the final
            // customer payment (e.g. product 16.90 → tax extracted 0.44 →
            // SubTotal shown 16.46 → customer pays 16.90).
            order.Tax = await _taxConfigurationService.CalculateTaxByOrderTypeAsync(
                itemsTotal, command.Type, cancellationToken);
            order.SubTotal = itemsTotal - order.Tax;
            order.DeliveryFee = command.Type == OrderType.Delivery ? FlatDeliveryFee : 0;

            await ApplyUserLimitDiscountAsync(order, command, userId, itemsTotal, cancellationToken);
            await ApplyCustomerDiscountAsync(order, userId, itemsTotal, cancellationToken);
        }

        ApplyTotal(order, command, itemsTotal);
    }

    private bool TryUsePreCalculatedBasketValues(Order order, CreateOrderCommand command)
    {
        if (!command.BasketSubTotal.HasValue ||
            !command.BasketTax.HasValue ||
            !command.BasketTotal.HasValue)
        {
            return false;
        }

        order.SubTotal = command.BasketSubTotal.Value;
        order.Tax = command.BasketTax.Value;
        order.Discount = command.BasketDiscount ?? 0;
        order.CustomerDiscountAmount = command.BasketCustomerDiscount ?? 0;

        _logger.LogInformation(
            "Using pre-calculated basket values for order: SubTotal={SubTotal}, Tax={Tax}, Discount={Discount}, CustomerDiscount={CustomerDiscount}",
            order.SubTotal, order.Tax, order.Discount, order.CustomerDiscountAmount);

        return true;
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

    private void ApplyTotal(Order order, CreateOrderCommand command, decimal itemsTotal)
    {
        if (command.BasketTotal.HasValue)
        {
            order.Total = command.BasketTotal.Value;
            _logger.LogInformation("Using pre-calculated basket total: {Total}", order.Total);
            return;
        }

        // Total = items + delivery − discounts − fidelity-points discount.
        // Tax is intentionally NOT added — see ApplyAsync's tax-flow comment.
        // FidelityPointsDiscount is 0 here; the handler may update Total
        // again after redemption sets it.
        var totalBeforeFidelity = itemsTotal + order.DeliveryFee - order.Discount - order.CustomerDiscountAmount;
        var calculatedTotal = totalBeforeFidelity - order.FidelityPointsDiscount;
        var hasActiveDiscount = PriceRoundingUtility.HasActiveDiscount(order.CustomerDiscountAmount + order.Discount);
        order.Total = PriceRoundingUtility.ApplySpecialRounding(calculatedTotal, hasActiveDiscount);
    }
}
