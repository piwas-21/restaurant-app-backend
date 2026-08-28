using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
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
    private readonly OrderSettings _orderSettings;
    private readonly ILogger<BasketPricingService> _logger;

    public BasketPricingService(
        ICustomerDiscountService customerDiscountService,
        IOptions<OrderSettings> orderSettings,
        ILogger<BasketPricingService> logger)
    {
        _customerDiscountService = customerDiscountService;
        _orderSettings = orderSettings.Value;
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

        // The delivery fee is read from the SAME OrderSettings the order-side pricing uses, so the
        // total the checkout page shows is the total the server charges. It used to be left at 0
        // here and applied only in OrderPricingService — harmless while that value was a dead
        // constant, but the moment a tenant sets OrderSettings:DeliveryFee it would charge a fee the
        // basket never displayed and the tender never covered. Basket.OrderType is nullable and only
        // Delivery attracts the fee, so an undecided basket shows none.
        basket.DeliveryFee = basket.OrderType == OrderType.Delivery ? _orderSettings.DeliveryFee : 0;

        // Calculate total before rounding (without tax since order type is not yet known)
        decimal amountAfterDiscount = basket.SubTotal - customerDiscountAmount - basket.Discount;
        decimal calculatedTotal = amountAfterDiscount + basket.DeliveryFee;

        // Apply special rounding for discounted customers
        basket.Total = PriceRoundingUtility.ApplySpecialRounding(calculatedTotal, hasDiscount);
    }

    public decimal CalculateIngredientCustomizationPrice(
        IEnumerable<ProductIngredient>? detailedIngredients,
        IReadOnlyCollection<Guid>? selectedIngredientIds,
        IReadOnlyDictionary<Guid, int>? ingredientQuantities,
        int sauceIncludedFree = 0)
    {
        if (detailedIngredients is null)
        {
            return 0;
        }

        // HashSet for O(1) membership checks inside the loop.
        var selected = selectedIngredientIds != null ? new HashSet<Guid>(selectedIngredientIds) : new HashSet<Guid>();
        decimal customizationPrice = 0;

        // One entry per CHARGED unit of a sauce row (a row of Kind = Sauce that this method actually
        // billed for). Built only when there is an allowance to spend, so the path every product on
        // production takes today allocates nothing and behaves byte-identically.
        // Deliberately a list of units, not of rows: "2 sauces free" means two units, and a guest
        // who takes two of the same sauce has spent the allowance just as surely as one who took two
        // different ones.
        List<(decimal Price, int DisplayOrder, Guid Id)>? chargeableSauceUnits =
            sauceIncludedFree > 0 ? new List<(decimal, int, Guid)>() : null;

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

            if (chargeableSauceUnits != null && ingredient.Kind == IngredientKind.Sauce && ingredient.Price > 0)
            {
                // How many units of THIS row the rule below is about to charge for. The two cases
                // mirror the two branches of the per-ingredient rule exactly — nothing is collected
                // that is not also billed, which is what makes the waiver incapable of inventing a
                // refund: it can only remove a charge that this same loop added. An unselected row
                // is charged for nothing, whichever branch it would take.
                int chargeableUnits = 0;
                if (isSelected)
                {
                    chargeableUnits = ingredient.IsIncludedInBasePrice ? Math.Max(0, quantity - 1) : quantity;
                }

                for (int unit = 0; unit < chargeableUnits; unit++)
                {
                    chargeableSauceUnits.Add((ingredient.Price, ingredient.DisplayOrder, ingredient.Id));
                }
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

        if (chargeableSauceUnits is { Count: > 0 })
        {
            // The allowance is spent on the MOST EXPENSIVE charged units first, for three reasons,
            // and the third is the security one. It is the customer-friendly reading of "N sauces
            // included". It is deterministic, so the same basket always prices the same. And it does
            // NOT depend on the order of the client-supplied selection array, which would otherwise
            // make the order of a JSON array a price lever.
            // Ties fall back to DisplayOrder (then Id, so the sort is total), which is the order the
            // guest sheet renders in — so the sheet's "Included" badge lands on the first row shown,
            // which is what the approved design draws.
            customizationPrice -= chargeableSauceUnits
                .OrderByDescending(u => u.Price)
                .ThenBy(u => u.DisplayOrder)
                .ThenBy(u => u.Id)
                .Take(sauceIncludedFree)
                .Sum(u => u.Price);
        }

        return customizationPrice;
    }
}
