using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Basket.Services;

public partial class BasketItemFactory
{
    public async Task<BasketItem> BuildMenuItemAsync(
        Product product, AddToBasketDto item, Guid basketId, OrderType? basketOrderType)
    {
        if (product.MenuDefinition == null)
            throw new NotFoundException("Menu definition not found");

        // Calculate total price including options
        decimal menuTotalPrice = product.BasePrice;
        var selectedOptions = item.SelectedMenuOptions ?? new List<SelectedMenuOptionDto>();
        var customerSteps = CustomerStepManifestStore.Read(product)?.Steps;

        // Basket and staff counter orders share one exact section rule. It validates required/min/max
        // counts, quantities, and that every option belongs to the section named by the request.
        menuTotalPrice += MenuBundleSelectionRules.ValidateAndSumOptionPrices(
            product.MenuDefinition.Sections, selectedOptions);

        var auditIdentifier = _currentUserService.GetAuditIdentifier();

        // Create Parent Basket Item
        var basketItem = new BasketItem
        {
            BasketId = basketId,
            ProductId = item.ProductId,
            ProductVariationId = item.ProductVariationId,
            Quantity = item.Quantity,
            UnitPrice = menuTotalPrice,
            ItemTotal = menuTotalPrice * item.Quantity,
            SpecialInstructions = item.SpecialInstructions,
            QuantityBasis = QuantityBasis.LineTotal,
            ConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
            CompositionRole = CompositionRole.Menu,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = auditIdentifier
        };

        // Batch-load every selected option's child product (with ingredients) in one query
        // instead of one round-trip per option (avoids N+1).
        var childProductIds = selectedOptions.Select(o => o.ItemId).Distinct().ToList();
        var childProducts = await LoadMenuChildProductsAsync(childProductIds);

        // §9.3: a combo being orderable on this channel says nothing about the components chosen
        // inside it, and the caller's guard only ever saw the combo. `OrderChannelGuard` already
        // walks children at order creation, so a line that gets past here is a dead end the guest
        // discovers at checkout rather than at add time.
        //
        // This closes the ADD-under-a-channel half only. The add-then-SWITCH half is still open:
        // `BasketChannelService.FindConflictsAsync` walks root lines only, so a combo added with no
        // channel chosen (permissive by design, and the dominant browse state) still reports zero
        // conflicts when the guest later picks a channel its components refuse. Tracked as §9.15.
        foreach (var childProduct in childProducts.Values)
        {
            BasketChannelGuard.EnsureOrderable(childProduct, basketOrderType);
        }

        // Create Child Basket Items for selected options. They are attached to the parent's
        // ChildBasketItems navigation rather than added here so a failed child leaves no saved row.
        var receiptOrder = product.MenuDefinition.Sections
            .OrderBy(section => section.DisplayOrder)
            .SelectMany(section => section.Items.OrderBy(row => row.DisplayOrder))
            .Select((row, index) => (row.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index);
        var resolvedOptions = ResolveMenuOptions(product, selectedOptions);
        var selectedOptionsByRowId = resolvedOptions.GroupBy(resolved => resolved.SectionItem.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var buildContext = new MenuBuildContext
        {
            ParentBasketItem = basketItem,
            RootItem = item,
            OptionsByRowId = selectedOptionsByRowId,
            ChildProducts = childProducts,
            CustomerSteps = customerSteps,
            ReceiptOrder = receiptOrder,
            BasketOrderType = basketOrderType,
            AuditIdentifier = auditIdentifier
        };
        var childBuild = BuildMenuChildren(buildContext, resolvedOptions, menuTotalPrice);
        menuTotalPrice = childBuild.MenuPrice;
        var totalCustomizationPrice = childBuild.CustomizationPrice;

        basketItem.UnitPrice = menuTotalPrice + totalCustomizationPrice;
        basketItem.ItemTotal = basketItem.UnitPrice * item.Quantity;
        basketItem.CustomizationPrice = totalCustomizationPrice;

        return basketItem;
    }

    private async Task<Dictionary<Guid, Product>> LoadMenuChildProductsAsync(IReadOnlyCollection<Guid> childProductIds)
    {
        var childProducts = await _context.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.DetailedIngredients)
            .Include(p => p.Variations)
            .Include(p => p.CustomizationGroups)
                .ThenInclude(group => group.IngredientOptions)
                    .ThenInclude(membership => membership.ProductIngredient)
            .Include(p => p.CustomizationGroups)
                .ThenInclude(group => group.ProductOptions)
                    // Nested product choices can inherit channel masks from their primary category.
                    .ThenInclude(membership => membership.OptionProduct.ProductCategories)
                        .ThenInclude(category => category.Category)
            // See the side-item load: without the inheritance chain the guard below resolves every
            // inheriting option as unrestricted, which is worse than no guard — it looks like one.
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Include(p => p.SuggestedSideItems)
            .Where(p => childProductIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);
        await LoadMenuSideProductsAsync(childProducts.Values);
        return childProducts;
    }

    private async Task LoadMenuSideProductsAsync(IEnumerable<Product> childProducts)
    {
        var memberships = childProducts.SelectMany(product => product.SuggestedSideItems
            .Select(membership => (Owner: product, Membership: membership))).ToList();
        var sideProductIds = memberships.Select(row => row.Membership.SideItemProductId)
            .Distinct().ToList();
        if (sideProductIds.Count == 0) return;

        var sideProducts = await _context.Products.AsNoTracking().AsSplitQuery()
            .Include(product => product.ProductCategories)
                .ThenInclude(category => category.Category)
            .Include(product => product.Variations)
            .Where(product => sideProductIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);
        foreach (var row in memberships)
        {
            if (sideProducts.TryGetValue(row.Membership.SideItemProductId, out var sideProduct))
            {
                row.Membership.SideItemProduct = sideProduct;
                continue;
            }

            // The former Include chain applied Product's soft-delete filter to this required
            // reference and omitted the association row. Keep that behavior in the batch path.
            row.Owner.SuggestedSideItems.Remove(row.Membership);
        }
    }


}
