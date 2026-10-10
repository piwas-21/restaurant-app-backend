using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using DomainBasket = RestaurantSystem.Domain.Entities.Basket;

namespace RestaurantSystem.Api.Features.Basket.Services;

/// <summary>
/// Default <see cref="IBasketMappingService"/>. This is a faithful extraction of
/// the <c>MapToBasketDtoAsync</c> logic that previously lived in
/// <c>BasketService</c>; behaviour is unchanged. It reads reference data
/// (side-item products) and recomputes the display discount, so it depends on
/// the <see cref="ApplicationDbContext"/> and <see cref="ICustomerDiscountService"/>.
/// </summary>
public class BasketMappingService : IBasketMappingService
{
    private readonly ApplicationDbContext _context;
    private readonly ICustomerDiscountService _customerDiscountService;
    private readonly ILogger<BasketMappingService> _logger;
    private readonly IBasketToOrderTranslator _orderTranslator;

    public BasketMappingService(
        ApplicationDbContext context,
        ICustomerDiscountService customerDiscountService,
        ILogger<BasketMappingService> logger,
        IBasketToOrderTranslator orderTranslator)
    {
        _context = context;
        _customerDiscountService = customerDiscountService;
        _logger = logger;
        _orderTranslator = orderTranslator;
    }

    public async Task<BasketDto> MapAsync(DomainBasket basket)
    {
        // Calculate customer discount if user is logged in
        decimal customerDiscountAmount = 0;
        string? customerDiscountName = null;

        if (basket.UserId.HasValue && basket.UserId.Value != Guid.Empty)
        {
            var customerDiscount = await _customerDiscountService.FindBestApplicableDiscountAsync(
                basket.UserId.Value,
                basket.SubTotal
            );

            if (customerDiscount != null)
            {
                customerDiscountAmount = _customerDiscountService.CalculateDiscountAmount(customerDiscount, basket.SubTotal);
                customerDiscountName = customerDiscount.Name;
            }
        }

        var selectedSideItemsByBasketItemId = await BasketSelectedSideMapper.MapAllAsync(
            _context, _logger, basket.Items);
        var allItems = new List<BasketItemDto>();
        foreach (var item in basket.Items
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.ProductId)
            .ThenBy(row => row.SpecialInstructions, StringComparer.Ordinal)
            .ThenBy(row => row.Id))
        {
            // Get ingredient names from product's detailed ingredients
            var productIngredients = item.Product?.DetailedIngredients ?? new List<ProductIngredient>();

            var selectedNames = item.SelectedIngredients?
                .Select(id => productIngredients.FirstOrDefault(pi => pi.Id == id)?.Name ?? id.ToString())
                .ToList();

            var addedNames = item.AddedIngredients?
                .Select(id => productIngredients.FirstOrDefault(pi => pi.Id == id)?.Name ?? id.ToString())
                .ToList();

            // Deserialize ingredient quantities
            var ingredientQuantities = DeserializeIngredientQuantities(item.IngredientQuantitiesJson, item.Id);
            var removedNames = BuildRemovedIngredientNames(
                productIngredients, ingredientQuantities, item.SelectedIngredients);

            allItems.Add(new BasketItemDto
            {
                Id = item.Id,
                ProductId = item.ProductId,
                ProductName = item.Product != null ? item.Product.Name : item.Menu?.Name ?? string.Empty,
                MenuId = item.MenuId,
                ProductDescription = item.Product != null ? item.Product.Description : item.Menu?.Description ?? string.Empty,
                ProductImageUrl = item.Product?.ImageUrl ?? string.Empty,
                ProductVariationId = item.ProductVariationId,
                SectionId = item.SectionId,
                MenuSectionItemId = item.MenuSectionItemId,
                ParentComponentMenuSectionItemId = item.ParentComponentMenuSectionItemId,
                QuantityBasis = item.QuantityBasis,
                ConfigurationScope = item.ConfigurationScope,
                CompositionRole = item.CompositionRole,
                PresentationLabel = item.PresentationLabel,
                PresentationOrder = item.PresentationOrder,
                VariationName = item.ProductVariation?.Name,
                // Descriptions is a non-nullable collection (initialised to []),
                // so only the ProductVariation qualifier needs the null-conditional.
                VariationContent = item.ProductVariation?.Descriptions.ToDictionary(
                    d => d.LanguageCode,
                    d => new BasketItemVariationContentDto(d.Name, d.Description)
                ),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                ItemTotal = item.ItemTotal,
                SpecialInstructions = item.SpecialInstructions,
                SelectedIngredients = item.SelectedIngredients,
                AddedIngredients = item.AddedIngredients,
                IngredientQuantities = ingredientQuantities,
                IngredientCompositionRoles = DeserializeIngredientRoles(item.IngredientCompositionRolesJson),
                CustomizationPrice = item.CustomizationPrice,
                SelectedIngredientNames = selectedNames,
                AddedIngredientNames = addedNames,
                RemovedIngredientNames = removedNames,
                SelectedSideItems = selectedSideItemsByBasketItemId.GetValueOrDefault(item.Id),
                ChildItems = MapChildItems(item.ChildBasketItems, selectedSideItemsByBasketItemId)
            });
        }

        // Build a HashSet of child item IDs (O(n)) so the root-item filter below is O(n)
        // instead of O(n²). Items whose ID appears in this set are bundle children and must
        // be excluded from the top-level list (they are already nested under ChildItems).
        var childItemIds = basket.Items
            .Where(bi => bi.ParentBasketItemId.HasValue)
            .Select(bi => (Guid?)bi.Id)
            .ToHashSet();

        var rootItems = allItems.Where(i => !childItemIds.Contains(i.Id)).ToList();

        return new BasketDto
        {
            Id = basket.Id,
            UserId = basket.UserId != Guid.Empty ? basket.UserId : null,
            SessionId = basket.SessionId,
            SubTotal = basket.SubTotal,
            Tax = basket.Tax,
            DeliveryFee = basket.DeliveryFee,
            Discount = basket.Discount,
            CustomerDiscount = customerDiscountAmount,
            CustomerDiscountName = customerDiscountName,
            Total = basket.Total,
            PromoCode = basket.PromoCode,
            TotalItems = basket.Items.Where(i => i.ParentBasketItemId == null).Sum(i => i.Quantity), // Count only root items? Or all? Usually root items (bundles) count as 1
            ExpiresAt = basket.ExpiresAt,
            Notes = basket.Notes,
            OrderType = basket.OrderType,
            PurchaseFingerprint = BasketPurchaseFingerprint.Compute(basket.OrderType, rootItems, _orderTranslator),
            Items = rootItems
        };
    }

    private Dictionary<Guid, int>? DeserializeIngredientQuantities(string? ingredientQuantitiesJson, Guid? basketItemId)
    {
        if (string.IsNullOrEmpty(ingredientQuantitiesJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<Guid, int>>(ingredientQuantitiesJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize ingredient quantities JSON for basket item {BasketItemId}", basketItemId);
            return null;
        }
    }

    private static Dictionary<Guid, RestaurantSystem.Domain.Common.Enums.CompositionRole>? DeserializeIngredientRoles(
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<Guid, RestaurantSystem.Domain.Common.Enums.CompositionRole>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps one bundle component. A child carries the same IDs, quantities, display names and
    /// removals as a root basket line: <c>OrderLineSummary</c> renders the tree recursively on the
    /// basket flyout, cart and checkout. Omitting its selected names silently hid a bundle option's
    /// added ingredients and sauces even though checkout persisted them (#150).
    /// </summary>
    private List<BasketItemDto> MapChildItems(
        IEnumerable<BasketItem> children,
        IReadOnlyDictionary<Guid, List<BasketSideItemDto>> selectedSideItemsByBasketItemId)
    {
        var mapped = new List<BasketItemDto>();
        foreach (var child in children)
            mapped.Add(MapChildItem(child, selectedSideItemsByBasketItemId));
        return mapped;
    }

    private BasketItemDto MapChildItem(
        BasketItem child,
        IReadOnlyDictionary<Guid, List<BasketSideItemDto>> selectedSideItemsByBasketItemId)
    {
        var childIngredients = child.Product?.DetailedIngredients ?? new List<ProductIngredient>();
        var childQuantities = DeserializeIngredientQuantities(child.IngredientQuantitiesJson, child.Id);
        var childSelectedNames = child.SelectedIngredients?
            .Select(id => childIngredients.FirstOrDefault(pi => pi.Id == id)?.Name ?? id.ToString())
            .ToList();

        return new BasketItemDto
        {
            Id = child.Id,
            ProductId = child.ProductId,
            ProductCustomizationOptionId = child.ProductCustomizationOptionId,
            SectionId = child.SectionId,
            MenuSectionItemId = child.MenuSectionItemId,
            ParentComponentMenuSectionItemId = child.ParentComponentMenuSectionItemId,
            QuantityBasis = child.QuantityBasis,
            ConfigurationScope = child.ConfigurationScope,
            CompositionRole = child.CompositionRole,
            PresentationLabel = child.PresentationLabel,
            PresentationOrder = child.PresentationOrder,
            ProductName = child.Product?.Name,
            ProductVariationId = child.ProductVariationId,
            VariationName = child.ProductVariation?.Name,
            VariationContent = child.ProductVariation?.Descriptions.ToDictionary(
                description => description.LanguageCode,
                description => new BasketItemVariationContentDto(description.Name, description.Description)),
            Quantity = child.Quantity,
            UnitPrice = child.UnitPrice,
            ItemTotal = child.ItemTotal,
            CustomizationPrice = child.CustomizationPrice,
            // Per-option ingredient customizations must round-trip through the cart,
            // or the checkout payload (and ultimately the kitchen ticket) loses them.
            // See issue #150.
            SpecialInstructions = child.SpecialInstructions,
            SelectedIngredients = child.SelectedIngredients,
            IngredientQuantities = childQuantities,
            IngredientCompositionRoles = DeserializeIngredientRoles(child.IngredientCompositionRolesJson),
            SelectedIngredientNames = childSelectedNames,
            RemovedIngredientNames = BuildRemovedIngredientNames(
                childIngredients, childQuantities, child.SelectedIngredients),
            SelectedSideItems = selectedSideItemsByBasketItemId.GetValueOrDefault(child.Id),
            ChildItems = MapChildItems(child.ChildBasketItems, selectedSideItemsByBasketItemId),
        };
    }

    /// <summary>
    /// The base-recipe ingredients the guest removed, so the cart can show "No onion" the way the
    /// order view already does (#363). Both read the same channel — a saved quantity of 0 — through
    /// the same <see cref="IngredientRecipeRules"/> test, so a 0 means the same thing on both.
    /// (They are not yet identical: the order view ALSO reports a required ingredient that is
    /// absent from the map, and this does not. That gap is tracked separately.)
    ///
    /// <para><b><paramref name="selectedIngredients"/> is the gate.</b> A saved quantity map is not
    /// evidence of a choice, so a line that arrives with no selection at all has nothing to report.
    /// This was load-bearing when it was written: re-order posts only product/quantity
    /// (<c>useReorder.ts</c>) and <c>LineCustomizationBuilder</c>'s regular-item branch backfilled a
    /// 0 for every unselected active optional-or-included ingredient anyway, so reading those back
    /// would have told a guest re-ordering a Margherita that they had removed the cheese. (That set
    /// is not the base recipe and is not meant to be: it is too broad by the paid add-ons, and too
    /// narrow by the required ingredients that are NOT flagged included-in-base, which get no entry
    /// at all and reach the order view only through its separate required-absent branch.) That
    /// defect is now fixed at
    /// the source (#303 — the builder's two branches gate alike), which makes this gate redundant
    /// for the re-order payload but not dead: an explicit quantity map posted WITHOUT a selection
    /// is still persisted verbatim, and this rule declines to read it. Keeping the gate also keeps
    /// the cart's answer independent of which producer wrote the map.</para>
    ///
    /// <para>Null when there is nothing to say, empty when there is something to say and the answer
    /// is "nothing was removed". Both ship as JSON (no global
    /// <c>DefaultIgnoreCondition</c> is configured), so a consumer must test <c>.length</c> rather
    /// than truthiness or it will render an empty "Removed:" label.</para>
    /// </summary>
    private static List<string>? BuildRemovedIngredientNames(
        IEnumerable<ProductIngredient> productIngredients,
        Dictionary<Guid, int>? ingredientQuantities,
        List<Guid>? selectedIngredients)
    {
        if (selectedIngredients == null || ingredientQuantities == null || ingredientQuantities.Count == 0)
        {
            return null;
        }

        return productIngredients
            .Where(pi => ingredientQuantities.TryGetValue(pi.Id, out var quantity)
                && IngredientRecipeRules.IsRemoved(pi, quantity))
            .Select(pi => pi.Name)
            .ToList();
    }
}
