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

        if (requested is null)
        {
            return baseline;
        }

        var maximum = baseline.MaxSelection;
        if (requested.ClearMaxSelection)
        {
            maximum = null;
        }
        else if (requested.MaxSelection.HasValue)
        {
            maximum = requested.MaxSelection;
        }

        return new OptionSetAttachmentSettings
        {
            MinSelection = requested.MinSelection ?? baseline.MinSelection,
            MaxSelection = maximum,
            IncludedFree = requested.IncludedFree ?? baseline.IncludedFree,
            DisplayOrder = requested.DisplayOrder ?? baseline.DisplayOrder
        };
    }

    public static void Validate(
        OptionSetKind kind,
        OptionSetAttachmentRole role,
        OptionSetAttachmentSettings settings,
        int entryCount)
    {
        ValidateIngredientRules(kind, settings);
        ValidateSelectionRules(kind, settings, entryCount);
        ValidateDisplayOrder(settings);
        ValidateIncludedFree(kind, role, settings);
        ValidateNoGroupLimits(kind, settings);
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
            ValidateOverrideValue(value);
            ValidateOverrideKind(kind, value);
        }
    }

    private static void ValidateIngredientRules(OptionSetKind kind, OptionSetAttachmentSettings settings)
    {
        if (kind == OptionSetKind.Ingredient && (settings.MinSelection > 0 || settings.MaxSelection.HasValue))
        {
            throw new BadRequestException("Ingredient exclusion sets cannot define required minimum or maximum selection");
        }
    }

    private static void ValidateSelectionRules(
        OptionSetKind kind,
        OptionSetAttachmentSettings settings,
        int entryCount)
    {
        if (kind is not (OptionSetKind.Sauce or OptionSetKind.BundleChoice))
        {
            return;
        }

        var minimum = settings.MinSelection ?? 0;
        if (minimum < 0 || settings.MaxSelection < minimum || settings.IncludedFree < 0)
        {
            throw new BadRequestException("Selection minimum, maximum, and included-free counts are inconsistent");
        }

        if (kind == OptionSetKind.BundleChoice)
        {
            if (settings.MaxSelection is null or <= 0 || settings.MaxSelection > entryCount)
            {
                throw new BadRequestException("A choice group needs a positive maximum no greater than its selected option count");
            }
        }
        else if (settings.MaxSelection > entryCount)
        {
            throw new BadRequestException("A sauce maximum cannot exceed its selected option count");
        }
    }

    private static void ValidateDisplayOrder(OptionSetAttachmentSettings settings)
    {
        if (settings.DisplayOrder is < 0)
        {
            throw new BadRequestException("Attachment display order cannot be negative");
        }
    }

    private static void ValidateIncludedFree(
        OptionSetKind kind,
        OptionSetAttachmentRole role,
        OptionSetAttachmentSettings settings)
    {
        var allowsIncludedFree = kind == OptionSetKind.Sauce
            || (kind == OptionSetKind.BundleChoice && role == OptionSetAttachmentRole.ProductChoice);
        if (!allowsIncludedFree && settings.IncludedFree.HasValue)
        {
            throw new BadRequestException("Included-free counts apply only to sauce or product-choice attachments");
        }

        if (kind == OptionSetKind.BundleChoice && role == OptionSetAttachmentRole.ProductChoice
            && settings.IncludedFree > settings.MaxSelection)
        {
            throw new BadRequestException("Included-free units cannot exceed the product-choice maximum");
        }
    }

    private static void ValidateNoGroupLimits(OptionSetKind kind, OptionSetAttachmentSettings settings)
    {
        if (kind is OptionSetKind.Ingredient or OptionSetKind.SuggestedSide
            && (settings.MinSelection.HasValue || settings.MaxSelection.HasValue))
        {
            throw new BadRequestException("This option-set kind cannot define group selection limits");
        }
    }

    private static void ValidateOverrideValue(OptionSetEntryOverride value)
    {
        if ((value.Name is not null && (string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 200))
            || value.DisplayOrder is < 0 || value.MaxQuantity is < 1 || value.Price is < 0m
            || value.AdditionalPrice is < 0m)
        {
            throw new BadRequestException("An option override contains an invalid name, order, quantity, or price");
        }
    }

    private static void ValidateOverrideKind(OptionSetKind kind, OptionSetEntryOverride value)
    {
        var hasIngredientOnly = value.IsOptional.HasValue || value.MaxQuantity.HasValue
            || value.Price.HasValue || value.IsIncludedInBasePrice.HasValue;
        var hasSideOnly = value.IsRequired.HasValue;
        var hasBundleOnly = value.AdditionalPrice.HasValue || value.IsDefault.HasValue;
        var fieldsMismatch = kind is OptionSetKind.Ingredient or OptionSetKind.Sauce
            ? hasSideOnly || hasBundleOnly
            : false;
        if (kind == OptionSetKind.SuggestedSide)
        {
            fieldsMismatch = hasIngredientOnly || hasBundleOnly;
        }
        else if (kind == OptionSetKind.BundleChoice)
        {
            fieldsMismatch = hasIngredientOnly || hasSideOnly;
        }
        if (fieldsMismatch)
        {
            throw new BadRequestException("Option override fields do not match the option-set kind");
        }
    }
}
