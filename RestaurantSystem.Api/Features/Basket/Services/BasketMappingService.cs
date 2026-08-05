using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.FidelityPoints.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using System.Text.Json;
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

    public BasketMappingService(
        ApplicationDbContext context,
        ICustomerDiscountService customerDiscountService,
        ILogger<BasketMappingService> logger)
    {
        _context = context;
        _customerDiscountService = customerDiscountService;
        _logger = logger;
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

        // Mapped sequentially (not Task.WhenAll): the per-item side-item lookup
        // below queries the shared ApplicationDbContext, and EF Core forbids
        // concurrent operations on one context instance — running these in
        // parallel throws once two items carry side items.
        var allItems = new List<BasketItemDto>();
        foreach (var item in basket.Items)
        {
            // Get ingredient names from product's detailed ingredients
            var productIngredients = item.Product?.DetailedIngredients ?? new List<ProductIngredient>();

            var selectedNames = item.SelectedIngredients?
                .Select(id => productIngredients.FirstOrDefault(pi => pi.Id == id)?.Name ?? id.ToString())
                .ToList();

            var addedNames = item.AddedIngredients?
                .Select(id => productIngredients.FirstOrDefault(pi => pi.Id == id)?.Name ?? id.ToString())
                .ToList();

            // Deserialize and fetch side items details
            List<BasketSideItemDto>? selectedSideItems = null;
            if (!string.IsNullOrEmpty(item.SelectedSideItemsJson))
            {
                try
                {
                    var selectedSides = JsonSerializer.Deserialize<List<SelectedSideItemDto>>(item.SelectedSideItemsJson);
                    if (selectedSides != null && selectedSides.Count > 0)
                    {
                        var sideItemIds = selectedSides.Select(s => s.Id).ToList();
                        var sideItems = await _context.Products
                            .Where(p => sideItemIds.Contains(p.Id))
                            .ToListAsync();

                        selectedSideItems = selectedSides.Select(selectedSide =>
                        {
                            var sideItem = sideItems.FirstOrDefault(s => s.Id == selectedSide.Id);
                            if (sideItem != null)
                            {
                                return new BasketSideItemDto
                                {
                                    Id = sideItem.Id,
                                    Name = sideItem.Name,
                                    Description = sideItem.Description,
                                    Price = sideItem.BasePrice,
                                    ImageUrl = sideItem.ImageUrl,
                                    Quantity = selectedSide.Quantity,
                                    SubTotal = sideItem.BasePrice * selectedSide.Quantity
                                };
                            }
                            return null;
                        }).OfType<BasketSideItemDto>().ToList();
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize side items JSON for basket item {BasketItemId}", item.Id);
                }
            }

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
                CustomizationPrice = item.CustomizationPrice,
                SelectedIngredientNames = selectedNames,
                AddedIngredientNames = addedNames,
                RemovedIngredientNames = removedNames,
                SelectedSideItems = selectedSideItems,
                ChildItems = item.ChildBasketItems.Select(MapChildItem).ToList()
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

    /// <summary>
    /// Maps one bundle component. Extracted from the inline initializer it used to be so the child
    /// can carry <see cref="BasketItemDto.RemovedIngredientNames"/> through the same helper the
    /// root uses — a component's removals were previously unreportable, because the child mapping
    /// set no name lists at all.
    ///
    /// <para><b>It still sets no SelectedIngredientNames, deliberately.</b> That would be the
    /// ADDED side, not #363's subject, and it is not a free addition: the cart pairs
    /// <c>selectedIngredientNames[i]</c> with <c>selectedIngredients[i]</c> positionally
    /// (<c>lineSummary.ts</c> — the only such site since frontend #189 deleted
    /// <c>CartItemCustomizations.tsx</c>, which this used to name as the second), and the list also labels every
    /// selection "Added" — including base-recipe ingredients the guest never added. Populating it
    /// would change checkout copy from a backend-only change with no paired frontend work. Removals
    /// carry no such contract: the list stands alone and pairs with nothing.</para>
    /// </summary>
    private BasketItemDto MapChildItem(BasketItem child)
    {
        var childIngredients = child.Product?.DetailedIngredients ?? new List<ProductIngredient>();
        var childQuantities = DeserializeIngredientQuantities(child.IngredientQuantitiesJson, child.Id);

        return new BasketItemDto
        {
            Id = child.Id,
            ProductId = child.ProductId,
            ProductName = child.Product?.Name,
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
            RemovedIngredientNames = BuildRemovedIngredientNames(
                childIngredients, childQuantities, child.SelectedIngredients),
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
