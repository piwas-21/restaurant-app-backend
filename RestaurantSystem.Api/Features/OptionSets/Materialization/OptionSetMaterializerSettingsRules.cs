using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetMaterializerSettingsRules
{
    public static OptionSetAttachmentSettings Merge(
        OptionSetKind kind,
        OptionSetAttachmentSettings baseline,
        OptionSetAttachmentSettings? requested)
    {
        if (requested?.ClearMaxSelection == true
            && (kind != OptionSetKind.Sauce || requested.MaxSelection.HasValue))
        {
            throw new BadRequestException("Only a sauce attachment may explicitly clear its maximum selection");
        }

        return requested is null ? baseline : new()
        {
            MinSelection = requested.MinSelection ?? baseline.MinSelection,
            MaxSelection = requested.ClearMaxSelection ? null : requested.MaxSelection ?? baseline.MaxSelection,
            IncludedFree = requested.IncludedFree ?? baseline.IncludedFree,
            DisplayOrder = requested.DisplayOrder ?? baseline.DisplayOrder,
            ClearMaxSelection = false
        };
    }

    public static void Validate(
        OptionSetKind kind,
        OptionSetAttachmentRole role,
        OptionSetAttachmentSettings settings,
        int entryCount)
    {
        if (kind == OptionSetKind.Ingredient && (settings.MinSelection > 0 || settings.MaxSelection.HasValue))
        {
            throw new BadRequestException("Ingredient exclusion sets cannot define required minimum or maximum selection");
        }

        if (kind is OptionSetKind.Sauce or OptionSetKind.BundleChoice)
        {
            var minimum = settings.MinSelection ?? 0;
            if (minimum < 0 || settings.MaxSelection < minimum || settings.IncludedFree < 0)
            {
                throw new BadRequestException("Selection minimum, maximum, and included-free counts are inconsistent");
            }

            if (kind == OptionSetKind.BundleChoice && (settings.MaxSelection is null or <= 0 || settings.MaxSelection > entryCount))
            {
                throw new BadRequestException("A choice group needs a positive maximum no greater than its selected option count");
            }

            if (kind == OptionSetKind.Sauce && settings.MaxSelection > entryCount)
            {
                throw new BadRequestException("A sauce maximum cannot exceed its selected option count");
            }
        }

        if (settings.DisplayOrder is < 0)
        {
            throw new BadRequestException("Attachment display order cannot be negative");
        }

        if (kind != OptionSetKind.Sauce
            && !(kind == OptionSetKind.BundleChoice && role == OptionSetAttachmentRole.ProductChoice)
            && settings.IncludedFree.HasValue)
        {
            throw new BadRequestException("Included-free counts apply only to sauce or product-choice attachments");
        }

        if (kind == OptionSetKind.BundleChoice && role == OptionSetAttachmentRole.ProductChoice
            && settings.IncludedFree > settings.MaxSelection)
        {
            throw new BadRequestException("Included-free units cannot exceed the product-choice maximum");
        }

        if (kind is OptionSetKind.Ingredient or OptionSetKind.SuggestedSide
            && (settings.MinSelection.HasValue || settings.MaxSelection.HasValue))
        {
            throw new BadRequestException("This option-set kind cannot define group selection limits");
        }
    }

    public static void ValidateOverrides(
        OptionSetKind kind,
        IReadOnlyDictionary<Guid, OptionSetEntryOverride>? overrides)
    {
        if (overrides is null)
        {
            return;
        }

        foreach (var value in overrides.Values)
        {
            if ((value.Name is not null && (string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 200))
                || value.DisplayOrder is < 0 || value.MaxQuantity is < 1 || value.Price is < 0m
                || value.AdditionalPrice is < 0m)
            {
                throw new BadRequestException("An option override contains an invalid name, order, quantity, or price");
            }

            var hasIngredientOnly = value.IsOptional.HasValue || value.MaxQuantity.HasValue
                || value.Price.HasValue || value.IsIncludedInBasePrice.HasValue;
            var hasSideOnly = value.IsRequired.HasValue;
            var hasBundleOnly = value.AdditionalPrice.HasValue || value.IsDefault.HasValue;
            if ((kind is OptionSetKind.Ingredient or OptionSetKind.Sauce && (hasSideOnly || hasBundleOnly))
                || (kind == OptionSetKind.SuggestedSide && (hasIngredientOnly || hasBundleOnly))
                || (kind == OptionSetKind.BundleChoice && (hasIngredientOnly || hasSideOnly)))
            {
                throw new BadRequestException("Option override fields do not match the option-set kind");
            }
        }
    }
}
