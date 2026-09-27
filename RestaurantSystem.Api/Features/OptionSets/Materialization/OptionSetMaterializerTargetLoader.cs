using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetMaterializerTargetLoader
{
    public static async Task<OptionSetTargetState> LoadAsync(
        ApplicationDbContext context,
        OptionSet set,
        OptionSetMaterializationTargetRequest target,
        string? idempotencyKey,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken)
    {
        ValidateTargetShape(set, target);
        var product = await context.Products.Include(item => item.MenuDefinition)
            .FirstOrDefaultAsync(item => item.Id == target.TargetProductId, cancellationToken)
            ?? throw new NotFoundException("Option-set target product", target.TargetProductId);
        var section = await LoadSectionAsync(context, target, cancellationToken);
        var customizationGroup = await LoadCustomizationGroupAsync(context, target, cancellationToken);
        var attachment = await context.OptionSetAttachments.Include(item => item.AppliedRows)
            .FirstOrDefaultAsync(item => item.Role == target.Role
                && item.TargetProductId == target.TargetProductId
                && item.TargetMenuSectionId == target.TargetMenuSectionId
                && item.TargetCustomizationGroupId == target.TargetCustomizationGroupId, cancellationToken);
        if (attachment is not null && attachment.OptionSetId != set.Id)
        {
            throw new ConflictException("This choice target is already attached to a different option set. Detach it before choosing another source set.");
        }

        var menuDefinition = section?.MenuDefinition ?? product.MenuDefinition;
        var isIdempotentReplay = !string.IsNullOrWhiteSpace(idempotencyKey)
            && attachment is not null
            && attachment.LastIdempotencyKey == idempotencyKey
            && attachment.AppliedSetVersion == set.Version;
        if (!isIdempotentReplay)
        {
            ValidateVersions(target, attachment, menuDefinition, customizationGroup);
        }
        if (set.Status != OptionSetStatus.Active && attachment is null)
        {
            throw new ConflictException("Archived option sets cannot be attached to new targets");
        }

        var isStagedTarget = stagedProductIds?.Contains(product.Id) == true;
        if (attachment is null && ((!product.IsActive || !product.IsAvailable) && !isStagedTarget || product.IsComponent))
        {
            throw new ConflictException("Inactive, unavailable, or internal products cannot receive a new option-set attachment");
        }

        var entries = SelectEntries(set, target);
        if (target.Role == OptionSetAttachmentRole.ProductChoice
            && entries.Any(entry => entry.ProductVariationId.HasValue))
        {
            throw new BadRequestException("Product customization groups cannot target a specific product variation");
        }
        if (target.Overrides?.Keys.Any(id => entries.All(entry => entry.Id != id)) == true)
        {
            throw new BadRequestException("An override must refer to an option selected for this target");
        }

        var defaultSettings = ExistingSettings(set.Kind, attachment, section, customizationGroup, product);
        var settings = OptionSetMaterializerSettingsRules.Merge(set.Kind, defaultSettings, target.Settings);
        OptionSetMaterializerSettingsRules.Validate(set.Kind, target.Role, settings, entries.Count);
        OptionSetMaterializerSettingsRules.ValidateOverrides(set.Kind, target.Overrides);
        return new OptionSetTargetState
        {
            Product = product,
            MenuDefinition = menuDefinition,
            Section = section,
            CustomizationGroup = customizationGroup,
            Attachment = attachment,
            SelectedEntries = entries,
            AppliedByEntry = attachment?.AppliedRows.ToDictionary(row => row.OptionSetEntryId) ?? [],
            CurrentSettings = defaultSettings,
            Settings = settings
        };
    }

    private static void ValidateTargetShape(OptionSet set, OptionSetMaterializationTargetRequest target)
    {
        var expectedRole = set.Kind switch
        {
            OptionSetKind.Ingredient => OptionSetAttachmentRole.Ingredient,
            OptionSetKind.Sauce => OptionSetAttachmentRole.Sauce,
            OptionSetKind.BundleChoice => OptionSetAttachmentRole.BundleChoice,
            OptionSetKind.SuggestedSide => OptionSetAttachmentRole.SuggestedSide,
            _ => throw new BadRequestException("The option-set kind is invalid")
        };
        if (target.Role != expectedRole
            && !(set.Kind == OptionSetKind.BundleChoice && target.Role == OptionSetAttachmentRole.ProductChoice))
        {
            throw new BadRequestException("The attachment role must match the option-set kind");
        }

        var isBundleChoice = target.Role == OptionSetAttachmentRole.BundleChoice;
        var isProductChoice = target.Role == OptionSetAttachmentRole.ProductChoice;
        if (target.TargetProductId == Guid.Empty || string.IsNullOrWhiteSpace(target.TargetKey)
            || target.TargetKey.Length > 120
            || isBundleChoice != target.TargetMenuSectionId.HasValue
            || isProductChoice != target.TargetCustomizationGroupId.HasValue
            || (isBundleChoice && target.TargetCustomizationGroupId.HasValue)
            || (isProductChoice && target.TargetMenuSectionId.HasValue))
        {
            throw new BadRequestException("Each target needs a stable key and the correct product/section identifiers");
        }
    }

    private static async Task<ProductCustomizationGroup?> LoadCustomizationGroupAsync(
        ApplicationDbContext context,
        OptionSetMaterializationTargetRequest target,
        CancellationToken cancellationToken)
    {
        if (target.TargetCustomizationGroupId is not Guid groupId)
        {
            return null;
        }

        var group = await context.ProductCustomizationGroups
            .Include(item => item.ProductOptions)
            .FirstOrDefaultAsync(item => item.Id == groupId, cancellationToken)
            ?? throw new NotFoundException("Product customization group", groupId);
        if (group.ProductId != target.TargetProductId)
        {
            throw new BadRequestException("The product-choice group must belong to the specified product");
        }

        return group;
    }

    private static async Task<MenuSection?> LoadSectionAsync(
        ApplicationDbContext context,
        OptionSetMaterializationTargetRequest target,
        CancellationToken cancellationToken)
    {
        if (target.TargetMenuSectionId is not Guid sectionId)
        {
            return null;
        }

        var section = await context.MenuSections.Include(item => item.Items)
                .Include(item => item.MenuDefinition.Product)
            .FirstOrDefaultAsync(item => item.Id == sectionId, cancellationToken)
            ?? throw new NotFoundException("Menu section", sectionId);
        if (section.MenuDefinition.ProductId != target.TargetProductId
            || section.MenuDefinition.Product.Type != ProductType.Menu)
        {
            throw new BadRequestException("The bundle-choice section must belong to the specified menu product");
        }

        return section;
    }

    private static void ValidateVersions(
        OptionSetMaterializationTargetRequest target,
        OptionSetAttachment? attachment,
        MenuDefinition? definition,
        ProductCustomizationGroup? customizationGroup)
    {
        if (target.ExpectedAttachmentVersion != attachment?.Version)
        {
            throw new ConflictException("The option-set attachment changed or already exists. Reload the target before applying.");
        }

        if (target.Role == OptionSetAttachmentRole.BundleChoice && definition is not null
            && (target.ExpectedMenuAuthoringVersion is not int expected
                || definition.AuthoringVersion != expected))
        {
            throw new ConflictException("The menu changed. Reload it and send its current authoring version.");
        }

        if (target.Role == OptionSetAttachmentRole.BundleChoice && definition is null && target.ExpectedMenuAuthoringVersion.HasValue)
        {
            throw new BadRequestException("A menu authoring version was supplied for a product without a menu definition");
        }

        if (target.Role == OptionSetAttachmentRole.ProductChoice
            && (customizationGroup is null || target.ExpectedCustomizationGroupVersion is not int expectedGroupVersion
                || customizationGroup.AuthoringVersion != expectedGroupVersion))
        {
            throw new ConflictException("The product-choice group changed. Reload the product and send its current authoring version.");
        }

        if (target.Role != OptionSetAttachmentRole.ProductChoice && target.ExpectedCustomizationGroupVersion.HasValue)
        {
            throw new BadRequestException("A customization-group version was supplied for a different target role");
        }

        if (target.Role == OptionSetAttachmentRole.ProductChoice && target.ExpectedMenuAuthoringVersion.HasValue)
        {
            throw new BadRequestException("A menu authoring version was supplied for a product-choice target");
        }
    }

    private static List<OptionSetEntry> SelectEntries(OptionSet set, OptionSetMaterializationTargetRequest target)
    {
        var enabled = set.Entries.Where(entry => entry.IsEnabled).ToList();
        if (target.EntryIds is null)
        {
            return enabled;
        }

        if (target.EntryIds.Distinct().Count() != target.EntryIds.Count)
        {
            throw new BadRequestException("Option-set entry IDs may not repeat in an attachment target");
        }

        var byId = enabled.ToDictionary(entry => entry.Id);
        if (target.EntryIds.Any(id => !byId.ContainsKey(id)))
        {
            throw new BadRequestException("Every selected option-set entry must be active and belong to this set");
        }

        return target.EntryIds.Select(id => byId[id]).ToList();
    }

    private static OptionSetAttachmentSettings ExistingSettings(
        OptionSetKind kind,
        OptionSetAttachment? attachment,
        MenuSection? section,
        ProductCustomizationGroup? customizationGroup,
        Product product)
    {
        var usesCardinality = kind is OptionSetKind.Sauce or OptionSetKind.BundleChoice;
        return new OptionSetAttachmentSettings
        {
            MinSelection = usesCardinality
                ? attachment?.MinSelection ?? section?.MinSelection ?? customizationGroup?.MinSelection ?? product.SauceMin
                : null,
            MaxSelection = usesCardinality
                ? attachment?.MaxSelection ?? section?.MaxSelection ?? customizationGroup?.MaxSelection ?? product.SauceMax
                : null,
            IncludedFree = ExistingIncludedFree(kind, attachment, customizationGroup, product),
            DisplayOrder = attachment?.DisplayOrder ?? (section?.DisplayOrder ?? customizationGroup?.DisplayOrder ?? 0)
        };
    }

    private static int? ExistingIncludedFree(
        OptionSetKind kind,
        OptionSetAttachment? attachment,
        ProductCustomizationGroup? customizationGroup,
        Product product)
    {
        if (kind == OptionSetKind.Sauce)
        {
            return attachment?.IncludedFree ?? product.SauceIncludedFree;
        }

        if (kind == OptionSetKind.BundleChoice && customizationGroup is not null)
        {
            return attachment?.IncludedFree ?? customizationGroup.IncludedFreeUnits;
        }

        return null;
    }

}
