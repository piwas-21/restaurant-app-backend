using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

internal static class OptionSetProductEntryRules
{
    public static void ValidateDefaults(OptionSetKind kind, OptionSetEntryDto entry)
    {
        if (entry.AdditionalPrice < 0m)
        {
            throw new BadRequestException("A bundle choice surcharge cannot be negative");
        }

        var hasIngredientFields = entry.GlobalIngredientId.HasValue || entry.Price != 0m
            || entry.IsIncludedInBasePrice || entry.MaxQuantity != 1 || !entry.IsOptional;
        var hasBundleFields = entry.AdditionalPrice != 0m || entry.IsDefault;
        if (kind == OptionSetKind.BundleChoice && (hasIngredientFields || entry.IsRequired))
        {
            throw new BadRequestException("Bundle-choice entries may set only an option price and default selection");
        }

        if (kind == OptionSetKind.SuggestedSide && (hasIngredientFields || hasBundleFields))
        {
            throw new BadRequestException("Suggested-side entries inherit their product price and cannot set ingredient or bundle rules");
        }
    }
}
