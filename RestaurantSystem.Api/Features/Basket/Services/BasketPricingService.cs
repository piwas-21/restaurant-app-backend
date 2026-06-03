using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Entities;
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
        ArgumentNullException.ThrowIfNull(basket);

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

    public decimal CalculateIngredientCustomizationPrice(
        IEnumerable<ProductIngredient>? detailedIngredients,
        IReadOnlyCollection<Guid>? selectedIngredientIds,
        IReadOnlyDictionary<Guid, int>? ingredientQuantities)
    {
        if (detailedIngredients is null)
        {
            return 0;
        }

        // HashSet for O(1) membership checks inside the loop.
        var selected = selectedIngredientIds != null ? new HashSet<Guid>(selectedIngredientIds) : new HashSet<Guid>();
        decimal customizationPrice = 0;

        foreach (var ingredient in detailedIngredients.Where(i => i.IsOptional && i.IsActive))
        {
            bool isSelected = selected.Contains(ingredient.Id);
            int quantity = 1;

            if (ingredientQuantities != null && ingredientQuantities.TryGetValue(ingredient.Id, out var qty))
            {
                quantity = qty;
            }

            // Clamp to [0, MaxQuantity]. The lower bound matters for security:
            // IngredientQuantities is client-supplied, and a negative value would
            // otherwise reduce the customization price (a price-tampering vector).
            if (quantity < 0)
            {
                quantity = 0;
            }
            else if (quantity > ingredient.MaxQuantity)
            {
                quantity = ingredient.MaxQuantity;
            }

            if (ingredient.IsIncludedInBasePrice)
            {
                // Ingredient price is included in base price for 1 quantity
                if (!isSelected)
                {
                    // Deselected: deduct the included quantity (1)
                    customizationPrice -= ingredient.Price;
                }
                else if (quantity > 1)
                {
                    // Selected with more than 1: add extra quantities beyond the free one
                    customizationPrice += ingredient.Price * (quantity - 1);
                }
                // quantity == 1: already in base price, no change
            }
            else
            {
                // Regular optional ingredient (not included in base) — add if selected
                if (isSelected)
                {
                    customizationPrice += ingredient.Price * quantity;
                }
            }
        }

        return customizationPrice;
    }
}
