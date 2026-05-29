using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using DomainBasket = RestaurantSystem.Domain.Entities.Basket;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Default <see cref="IBasketPricingService"/>. The calculation here is a faithful extraction of
/// the totals logic that previously lived inline in <c>BasketService.RecalculateBasketTotalsAsync</c>;
/// behaviour is unchanged. Persistence (loading the basket with its items and saving) stays in
/// <c>BasketService</c>, which keeps this service free of <c>DbContext</c> and unit-testable.
/// </summary>
public class BasketPricingService : IBasketPricingService
{
    private readonly ICustomerDiscountService _customerDiscountService;
    private readonly ILogger<BasketPricingService> _logger;

    public BasketPricingService(
        ICustomerDiscountService customerDiscountService,
        ILogger<BasketPricingService> logger)
    {
        _customerDiscountService = customerDiscountService;
        _logger = logger;
    }

    public async Task ApplyTotalsAsync(DomainBasket basket, CancellationToken cancellationToken = default)
    {
        decimal subTotal = 0;

        foreach (var item in basket.Items)
        {
            subTotal += item.ItemTotal;
        }

        basket.SubTotal = subTotal;

        // Calculate customer discount if user is logged in
        decimal customerDiscountAmount = 0;
        bool hasDiscount = false;

        if (basket.UserId.HasValue && basket.UserId.Value != Guid.Empty)
        {
            var customerDiscount = await _customerDiscountService.FindBestApplicableDiscountAsync(
                basket.UserId.Value,
                subTotal,
                cancellationToken
            );

            if (customerDiscount != null)
            {
                customerDiscountAmount = _customerDiscountService.CalculateDiscountAmount(customerDiscount, subTotal);
                hasDiscount = PriceRoundingUtility.HasActiveDiscount(customerDiscountAmount);

                _logger.LogInformation(
                    "Applied customer discount '{DiscountName}' (ID: {DiscountId}) to basket {BasketId}: {DiscountAmount:C}",
                    customerDiscount.Name,
                    customerDiscount.Id,
                    basket.Id,
                    customerDiscountAmount
                );
            }
        }

        // Store the customer discount in the basket
        basket.CustomerDiscount = customerDiscountAmount;

        // Tax will be calculated later during order creation when order type is known
        // This is important for Swiss tax compliance (different rates for Dine-In vs Takeaway/Delivery)
        basket.Tax = 0;

        // Calculate total before rounding (without tax since order type is not yet known)
        decimal amountAfterDiscount = basket.SubTotal - customerDiscountAmount - basket.Discount;
        decimal calculatedTotal = amountAfterDiscount + basket.DeliveryFee;

        // Apply special rounding for discounted customers
        basket.Total = PriceRoundingUtility.ApplySpecialRounding(calculatedTotal, hasDiscount);
    }
}
