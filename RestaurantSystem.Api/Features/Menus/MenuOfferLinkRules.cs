using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus;

/// <summary>
/// Authoritative validation for menu-to-parent offer relationships. The relation is presentation
/// metadata, but invalid links can hide a sellable offer or create ambiguous variation pricing, so
/// every writer (bundle create/update and the narrow link command) uses these rules.
/// </summary>
public static class MenuOfferLinkRules
{
    public static async Task EnsureValidAsync(
        ApplicationDbContext context,
        Guid menuProductId,
        Guid? parentOfferProductId,
        Guid? parentOfferVariationId,
        CancellationToken cancellationToken,
        bool? menuIsComponent = null)
    {
        EnsureParentReferenceIsValid(menuProductId, parentOfferProductId, parentOfferVariationId);

        if (!parentOfferProductId.HasValue)
        {
            return;
        }

        await EnsureMenuCanBeLinkedAsync(context, menuProductId, menuIsComponent, cancellationToken);
        await EnsureParentCanBeAnchorAsync(context, parentOfferProductId.Value, cancellationToken);
        await EnsureParentVariationCanBeLinkedAsync(
            context, parentOfferProductId.Value, parentOfferVariationId, cancellationToken);
        await EnsureNoDuplicateAlternativeAsync(
            context, menuProductId, parentOfferProductId.Value, parentOfferVariationId, cancellationToken);
    }

    private static void EnsureParentReferenceIsValid(
        Guid menuProductId,
        Guid? parentOfferProductId,
        Guid? parentOfferVariationId)
    {
        if (!parentOfferProductId.HasValue && parentOfferVariationId.HasValue)
        {
            throw new BadRequestException("A parent offer product is required when a variation is supplied");
        }

        if (parentOfferProductId.HasValue && menuProductId == parentOfferProductId.Value)
        {
            throw new BadRequestException("A menu cannot be linked to itself");
        }
    }

    private static async Task EnsureMenuCanBeLinkedAsync(
        ApplicationDbContext context,
        Guid menuProductId,
        bool? menuIsComponent,
        CancellationToken cancellationToken)
    {
        // Read the child in the same transaction as the relationship write. This matters for the
        // narrow PATCH path: a caller can otherwise read a non-component menu before a concurrent
        // product update turns it into a component, then leave an invalid component alternative
        // behind. UpdateProduct passes the requested component state as an override because its
        // own product row has not been mutated yet.
        var storedIsComponent = await context.Products
            .AsNoTracking()
            .Where(product => product.Id == menuProductId && !product.IsDeleted)
            .Select(product => product.IsComponent)
            .FirstOrDefaultAsync(cancellationToken);

        if (storedIsComponent || menuIsComponent == true)
        {
            throw new BadRequestException(
                "Unlink this menu before making it a component");
        }

        // The child being linked must not already anchor alternatives. This is distinct from the
        // parent checks below: an independent bundle can validly be selected as an anchor first,
        // but once it has children it cannot itself become a menu alternative without creating a
        // chain (A -> B -> C) that the family read contract cannot flatten safely.
        if (await context.MenuDefinitions
                .AnyAsync(definition => definition.ParentOfferProductId == menuProductId,
                    cancellationToken))
        {
            throw new BadRequestException("A menu with alternatives cannot be linked upward");
        }
    }

    private static async Task EnsureParentCanBeAnchorAsync(
        ApplicationDbContext context,
        Guid parentOfferProductId,
        CancellationToken cancellationToken)
    {
        var parent = await context.Products
            .Include(product => product.MenuDefinition)
            .FirstOrDefaultAsync(product => product.Id == parentOfferProductId, cancellationToken);

        if (parent is null || parent.IsDeleted)
        {
            throw new NotFoundException("Parent offer product not found");
        }

        if (parent.IsComponent)
        {
            throw new BadRequestException("A bundle component cannot be an offer family anchor");
        }

        // A standalone bundle is a valid anchor (e.g. an existing Tacos bundle). A menu that is
        // already linked to another parent is not: allowing it would create a chain that the guest
        // catalogue cannot represent without nested purchase modes.
        if (parent.Type == ProductType.Menu && parent.MenuDefinition?.ParentOfferProductId.HasValue == true)
        {
            throw new BadRequestException("A linked menu cannot be used as another offer's parent");
        }
    }

    private static async Task EnsureParentVariationCanBeLinkedAsync(
        ApplicationDbContext context,
        Guid parentOfferProductId,
        Guid? parentOfferVariationId,
        CancellationToken cancellationToken)
    {
        if (!parentOfferVariationId.HasValue)
        {
            return;
        }

        var variation = await context.ProductVariations
            .FirstOrDefaultAsync(candidate => candidate.Id == parentOfferVariationId.Value
                && !candidate.IsDeleted, cancellationToken);

        if (variation is null)
        {
            throw new NotFoundException("Parent offer variation not found");
        }

        if (variation.ProductId != parentOfferProductId)
        {
            throw new BadRequestException("Parent offer variation does not belong to the parent product");
        }

        if (!variation.IsActive)
        {
            throw new BadRequestException("Parent offer variation is not active");
        }
    }

    private static async Task EnsureNoDuplicateAlternativeAsync(
        ApplicationDbContext context,
        Guid menuProductId,
        Guid parentOfferProductId,
        Guid? parentOfferVariationId,
        CancellationToken cancellationToken)
    {
        var existingLinks = await context.MenuDefinitions
            .Where(definition => definition.ParentOfferProductId == parentOfferProductId
                && definition.ProductId != menuProductId)
            .Select(definition => definition.ParentOfferVariationId)
            .ToListAsync(cancellationToken);

        if (existingLinks.Contains(parentOfferVariationId))
        {
            throw new BadRequestException("This parent offer already has a menu alternative for that variation");
        }
    }

    public static async Task EnsureCanDeactivateAsync(
        ApplicationDbContext context,
        Guid productId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        if (isActive)
        {
            return;
        }

        var hasParentAlternatives = await context.MenuDefinitions
            .AnyAsync(definition => definition.ParentOfferProductId == productId, cancellationToken);

        var isLinkedAlternative = await context.MenuDefinitions
            .AnyAsync(definition => definition.ProductId == productId
                && definition.ParentOfferProductId.HasValue,
                cancellationToken);

        if (hasParentAlternatives)
        {
            throw new BadRequestException(
                "Unlink or reassign this offer's menu alternatives before archiving it");
        }

        if (isLinkedAlternative)
        {
            throw new BadRequestException(
                "Unlink this menu alternative before archiving it");
        }
    }

    public static async Task EnsureCanBecomeComponentAsync(
        ApplicationDbContext context,
        Guid productId,
        bool isComponent,
        CancellationToken cancellationToken)
    {
        if (!isComponent)
        {
            return;
        }

        var hasAlternatives = await context.MenuDefinitions
            .AnyAsync(definition => definition.ParentOfferProductId == productId, cancellationToken);

        if (hasAlternatives)
        {
            throw new BadRequestException(
                "Unlink or reassign this offer's menu alternatives before making it a component");
        }

        var isLinkedAlternative = await context.MenuDefinitions
            .AnyAsync(definition => definition.ProductId == productId
                && definition.ParentOfferProductId.HasValue,
                cancellationToken);

        if (isLinkedAlternative)
        {
            throw new BadRequestException(
                "Unlink this menu alternative before making it a component");
        }
    }

    public static async Task EnsureCanChangeTypeAsync(
        ApplicationDbContext context,
        Product product,
        ProductType targetType,
        CancellationToken cancellationToken)
    {
        if (targetType == ProductType.Menu)
        {
            return;
        }

        var hasParentAlternatives = await context.MenuDefinitions
            .AnyAsync(definition => definition.ParentOfferProductId == product.Id, cancellationToken);

        if (product.MenuDefinition?.ParentOfferProductId.HasValue == true || hasParentAlternatives)
        {
            throw new BadRequestException(
                "Unlink this menu relationship before changing its product type");
        }
    }

    public static async Task EnsureCanDeactivateVariationAsync(
        ApplicationDbContext context,
        Guid variationId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        if (isActive)
        {
            return;
        }

        await EnsureCanDeactivateVariationsAsync(context, [variationId], cancellationToken);
    }

    public static async Task EnsureCanDeactivateVariationsAsync(
        ApplicationDbContext context,
        IReadOnlyCollection<Guid> variationIds,
        CancellationToken cancellationToken)
    {
        if (variationIds.Count == 0)
        {
            return;
        }

        var menuReferences = await context.MenuDefinitions
            .Where(definition => definition.ParentOfferVariationId.HasValue
                && variationIds.Contains(definition.ParentOfferVariationId ?? Guid.Empty))
            .Select(definition => definition.ParentOfferVariationId)
            .ToListAsync(cancellationToken);
        var sectionReferences = await context.MenuSectionItems
            .Where(item => item.ProductVariationId.HasValue
                && variationIds.Contains(item.ProductVariationId ?? Guid.Empty))
            .Select(item => item.ProductVariationId)
            .ToListAsync(cancellationToken);

        var referencedVariationIds = menuReferences
            .Concat(sectionReferences)
            .OfType<Guid>()
            .ToHashSet();
        if (referencedVariationIds.Overlaps(variationIds))
        {
            throw new BadRequestException(
                "Unlink or reassign this variation's menu references before archiving it");
        }
    }
}
