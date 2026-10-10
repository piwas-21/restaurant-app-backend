using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <inheritdoc />
public partial class OrderItemFactory : IOrderItemFactory
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILineCustomizationBuilder _lineCustomizationBuilder;
    private readonly ITenantFeatures _tenantFeatures;

    public OrderItemFactory(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILineCustomizationBuilder lineCustomizationBuilder,
        ITenantFeatures tenantFeatures)
    {
        _context = context;
        _currentUserService = currentUserService;
        _lineCustomizationBuilder = lineCustomizationBuilder;
        _tenantFeatures = tenantFeatures;
    }

    public async Task<string?> AddItemAsync(
        Order order, CreateOrderItemDto itemDto, bool itemsAreServerPriced,
        CancellationToken cancellationToken, bool allowStaffPrices = true)
    {
        // Prices in the DTO are honoured from two sources and no others: the persisted basket
        // (itemsAreServerPriced), and a staff member standing behind the till. Everyone else gets
        // catalogue pricing. The staff carve-out is the same one OrderPaymentBuilder already draws
        // for tenders — the POS legitimately hand-builds composed lines, and taking that away in
        // the name of guarding an anonymous endpoint would break the till instead.
        // Legacy callers retain the staff carve-out for composed lines. The authenticated counter
        // contract opts out explicitly: its payload is still a hand-built request, so only catalogue
        // values (or values prepared by a server-owned quote) may affect money.
        var pricesAreTrusted = itemsAreServerPriced
            || (allowStaffPrices && _currentUserService.IsStaff);
        // Only the basket translator is allowed to freeze authored composition semantics. Staff
        // prices can be trusted for the till, but a hand-built order DTO is not a source of stable
        // manifest identity or per-parent quantity guarantees.
        var metadataAreTrusted = itemsAreServerPriced;

        if (itemDto.MenuId.HasValue)
        {
            return await AddMenuItemAsync(order, itemDto, pricesAreTrusted, metadataAreTrusted, cancellationToken);
        }
        if (itemDto.ProductId.HasValue)
        {
            // A composed line is REFUSED on the untrusted path rather than priced from the
            // catalogue. Its real price is the parent's base plus the selected options', and those
            // option prices live in the menu definition — `product.BasePrice` alone cannot express
            // it. Falling back to the base price would silently UNDERCHARGE the bundle (measured:
            // 8.00 against a true 12.98), trading a large hole for a smaller one. Refusing keeps
            // the rule honest: this endpoint accepts only what it can price itself.
            //
            // The real checkout is unaffected — /from-basket is server-priced, and its bundles are
            // covered end to end by BasketToOrderIntegrationTest and OrderLineCustomizationPriceTests.
            // Two shapes are refused, not one. Child rows are the obvious composed line — but a
            // BUNDLE PARENT POSTED ALONE has no children and would slip through to be priced at
            // `product.BasePrice`, which is exactly the 8.00-against-a-true-12.98 undercharge this
            // guard exists to prevent. ProductType.Menu is what makes a product a bundle.
            if (!pricesAreTrusted &&
                (itemDto.ChildItems is { Count: > 0 } || await IsBundleAsync(itemDto.ProductId.Value, cancellationToken)))
            {
                return "A composed item cannot be ordered through this endpoint; check out from the basket instead.";
            }

            await AddProductItemRecursiveAsync(order, itemDto, parentItem: null, pricesAreTrusted, metadataAreTrusted,
                new Dictionary<Guid, Guid>(), cancellationToken);
        }
        // Neither MenuId nor ProductId: preserve the original fall-through.
        return null;
    }

    private Task<bool> IsBundleAsync(Guid productId, CancellationToken cancellationToken) =>
        _context.Products.AnyAsync(
            p => p.Id == productId && !p.IsDeleted && p.Type == ProductType.Menu, cancellationToken);

    private async Task AddProductItemRecursiveAsync(
        Order order,
        CreateOrderItemDto itemDto,
        OrderItem? parentItem,
        bool pricesAreTrusted,
        bool metadataAreTrusted,
        Dictionary<Guid, Guid> explicitParentRefs,
        CancellationToken cancellationToken)
    {
        var product = await LoadProductAsync(itemDto.ProductId, cancellationToken);

        if (parentItem is null)
        {
            BasketComponentGuard.EnsureNotOrderedAlone(product);
        }

        // What the line SAID about its ingredients, resolved against the recipe (#430). When it
        // carried a structured selection the server prices the line itself, so the declared
        // UnitPrice and CustomizationPrice are both dropped — `Price is not null` is that verdict.
        // See OrderLineIngredientChoice for which lines it covers and why the rest are excluded.
        var choice = OrderLineIngredientChoice.Resolve(
            _lineCustomizationBuilder, itemDto, product, isRootLine: parentItem == null);

        var orderItem = await CreateProductOrderItemAsync(
            itemDto, product, parentItem, choice, pricesAreTrusted, metadataAreTrusted, cancellationToken);

        order.Items.Add(orderItem);
        RememberExplicitParentReference(orderItem, itemDto, metadataAreTrusted, explicitParentRefs);
        await AddChildItemsAsync(order, itemDto, orderItem, pricesAreTrusted, metadataAreTrusted,
            explicitParentRefs, cancellationToken);
        AssignExplicitComponentParentsIfBundle(orderItem, itemDto, product, parentItem, order.Items,
            explicitParentRefs);
    }

    private async Task<Product> LoadProductAsync(Guid? productId, CancellationToken cancellationToken)
    {
        // DetailedIngredients is loaded for the ingredient snapshot below, not for pricing — money
        // is settled before this factory runs (see ResolvePricing). Sibling collections, hence split.
        var product = _context.Products.Local.FirstOrDefault(
            candidate => candidate.Id == productId && !candidate.IsDeleted);
        product ??= await _context.Products
            .Include(candidate => candidate.Variations)
            .Include(candidate => candidate.DetailedIngredients)
            .Include(candidate => candidate.CustomizationGroups)
                .ThenInclude(group => group.IngredientOptions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(candidate => candidate.Id == productId && !candidate.IsDeleted,
                cancellationToken);

        // Throws — matches the original recursive method's behaviour for both top-level and nested
        // products. The top-level not-found-as-Failure semantics only applies to menus.
        return product ?? throw new NotFoundException($"Product {productId} not found");
    }

    private static void RememberExplicitParentReference(
        OrderItem orderItem,
        CreateOrderItemDto itemDto,
        bool metadataAreTrusted,
        Dictionary<Guid, Guid> explicitParentRefs)
    {
        if (metadataAreTrusted && itemDto.ParentComponentMenuSectionItemId.HasValue)
            explicitParentRefs.Add(orderItem.Id, itemDto.ParentComponentMenuSectionItemId.Value);
    }

    private async Task AddChildItemsAsync(
        Order order,
        CreateOrderItemDto itemDto,
        OrderItem orderItem,
        bool pricesAreTrusted,
        bool metadataAreTrusted,
        Dictionary<Guid, Guid> explicitParentRefs,
        CancellationToken cancellationToken)
    {
        if (itemDto.ChildItems is null)
            return;

        foreach (var childDto in itemDto.ChildItems)
        {
            await AddProductItemRecursiveAsync(order, childDto, orderItem, pricesAreTrusted, metadataAreTrusted,
                explicitParentRefs, cancellationToken);
        }
    }

    private static void AssignExplicitComponentParentsIfBundle(
        OrderItem orderItem,
        CreateOrderItemDto itemDto,
        Product product,
        OrderItem? parentItem,
        IEnumerable<OrderItem> orderItems,
        Dictionary<Guid, Guid> explicitParentRefs)
    {
        if (parentItem is null && product.Type == ProductType.Menu && itemDto.ChildItems is { Count: > 0 })
            AssignExplicitComponentParents(orderItem, orderItems, explicitParentRefs);
    }

    private static void AssignExplicitComponentParents(
        OrderItem bundle, IEnumerable<OrderItem> orderItems, Dictionary<Guid, Guid> explicitParentRefs)
    {
        var directChildren = orderItems.Where(item => item.ParentOrderItemId == bundle.Id).ToList();
        foreach (var child in directChildren.Where(item => explicitParentRefs.ContainsKey(item.Id)))
        {
            var parentMenuSectionItemId = explicitParentRefs[child.Id];
            var parent = directChildren.SingleOrDefault(candidate =>
                candidate.MenuSectionItemId == parentMenuSectionItemId
                && candidate.Kind == OrderItemKind.BundleChild);
            if (parent is null)
                throw new BadRequestException("A dependent bundle choice references a missing selected component.");
            if (parent.CompositionRole != CompositionRole.Dish)
                throw new BadRequestException("A dependent bundle choice must reference an explicit Dish component.");

            child.ParentComponentOrderItemId = parent.Id;
            child.PresentationLabel = parent.PresentationLabel ?? parent.ProductName;
        }
    }


}
