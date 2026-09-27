using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetMaterializerTargetSettings
{
    public static void ApplyAttachmentSettings(
        OptionSetKind kind,
        OptionSetTargetState state,
        OptionSetAttachment attachment,
        OptionSetMaterializationTargetRequest target,
        string audit,
        DateTime now)
    {
        attachment.MinSelection = state.Settings.MinSelection;
        attachment.MaxSelection = state.Settings.MaxSelection;
        attachment.IncludedFree = state.Settings.IncludedFree;
        attachment.DisplayOrder = state.Settings.DisplayOrder ?? 0;
        attachment.IntentionalDifferenceReason = string.IsNullOrWhiteSpace(target.IntentionalDifferenceReason)
            ? attachment.IntentionalDifferenceReason : target.IntentionalDifferenceReason.Trim();
        if (kind == OptionSetKind.Sauce)
        {
            state.Product.SauceMin = state.Settings.MinSelection ?? 0;
            state.Product.SauceMax = state.Settings.MaxSelection;
            state.Product.SauceIncludedFree = state.Settings.IncludedFree ?? 0;
            state.Product.UpdatedAt = now;
            state.Product.UpdatedBy = audit;
        }

        if (kind == OptionSetKind.BundleChoice && state.Section is not null)
        {
            state.Section.MinSelection = state.Settings.MinSelection ?? 0;
            state.Section.MaxSelection = state.Settings.MaxSelection ?? 1;
            state.Section.IsRequired = state.Section.MinSelection > 0;
            state.Section.DisplayOrder = state.Settings.DisplayOrder ?? state.Section.DisplayOrder;
        }
    }

    public static void ApplyProductChoiceSettings(
        ProductCustomizationGroup group,
        OptionSetAttachmentSettings settings)
    {
        group.MinSelection = settings.MinSelection ?? 0;
        group.MaxSelection = settings.MaxSelection ?? 1;
        group.IsRequired = group.MinSelection > 0;
        group.IncludedFreeUnits = settings.IncludedFree ?? 0;
        group.DisplayOrder = settings.DisplayOrder ?? group.DisplayOrder;
    }
}
