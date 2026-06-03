using Microsoft.EntityFrameworkCore;
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

            var excludedNames = item.ExcludedIngredients?
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
            Dictionary<Guid, int>? ingredientQuantities = null;
            if (!string.IsNullOrEmpty(item.IngredientQuantitiesJson))
            {
                try
                {
                    ingredientQuantities = JsonSerializer.Deserialize<Dictionary<Guid, int>>(item.IngredientQuantitiesJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize ingredient quantities JSON for basket item {BasketItemId}", item.Id);
                }
            }

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
                ExcludedIngredients = item.ExcludedIngredients,
                AddedIngredients = item.AddedIngredients,
                IngredientQuantities = ingredientQuantities,
                CustomizationPrice = item.CustomizationPrice,
                SelectedIngredientNames = selectedNames,
                ExcludedIngredientNames = excludedNames,
                AddedIngredientNames = addedNames,
                SelectedSideItems = selectedSideItems,
                ChildItems = item.ChildBasketItems.Select(child => new BasketItemDto
                {
                    Id = child.Id,
                    ProductId = child.ProductId,
                    ProductName = child.Product?.Name,
                    Quantity = child.Quantity,
                    UnitPrice = child.UnitPrice,
                    ItemTotal = child.ItemTotal,
                    CustomizationPrice = child.CustomizationPrice,
                    // Map other properties if needed, but for menu options these are usually minimal
                }).ToList()
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
            Items = rootItems
        };
    }
}
