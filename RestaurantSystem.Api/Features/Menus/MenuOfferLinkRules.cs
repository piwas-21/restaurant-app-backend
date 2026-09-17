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
        CancellationToken cancellationToken)
    {
        if (!parentOfferProductId.HasValue && parentOfferVariationId.HasValue)
        {
            throw new BadRequestException("A parent offer product is required when a variation is supplied");
        }

        if (!parentOfferProductId.HasValue)
        {
            return;
        }

        if (menuProductId == parentOfferProductId.Value)
        {
            throw new BadRequestException("A menu cannot be linked to itself");
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

        var parent = await context.Products
            .Include(product => product.MenuDefinition)
            .FirstOrDefaultAsync(product => product.Id == parentOfferProductId.Value, cancellationToken);

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

        if (parentOfferVariationId.HasValue)
        {
            var variation = await context.ProductVariations
                .FirstOrDefaultAsync(candidate => candidate.Id == parentOfferVariationId.Value
                    && !candidate.IsDeleted, cancellationToken);

            if (variation is null)
            {
                throw new NotFoundException("Parent offer variation not found");
            }

            if (variation.ProductId != parentOfferProductId.Value)
            {
                throw new BadRequestException("Parent offer variation does not belong to the parent product");
            }

            if (!variation.IsActive)
            {
                throw new BadRequestException("Parent offer variation is not active");
            }
        }

        var existingLinks = await context.MenuDefinitions
            .Where(definition => definition.ParentOfferProductId == parentOfferProductId.Value
                && definition.ProductId != menuProductId)
            .Select(definition => definition.ParentOfferVariationId)
            .ToListAsync(cancellationToken);

        if (existingLinks.Any(existingVariationId => existingVariationId == parentOfferVariationId))
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

        var hasAlternatives = await context.MenuDefinitions
            .AnyAsync(definition => definition.ParentOfferProductId == productId, cancellationToken);

        if (hasAlternatives)
        {
            throw new BadRequestException(
                "Unlink or reassign this offer's menu alternatives before archiving it");
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

    public static void EnsureCanChangeType(Product product, ProductType targetType)
    {
        if (targetType != ProductType.Menu
            && product.MenuDefinition?.ParentOfferProductId.HasValue == true)
        {
            throw new BadRequestException(
                "Unlink this menu alternative before changing its product type");
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

        var hasAlternatives = await context.MenuDefinitions
            .AnyAsync(definition => definition.ParentOfferVariationId == variationId, cancellationToken)
            || await context.MenuSectionItems
                .AnyAsync(item => item.ProductVariationId == variationId, cancellationToken);

        if (hasAlternatives)
        {
            throw new BadRequestException(
                "Unlink or reassign this variation's menu references before archiving it");
        }
    }
}
