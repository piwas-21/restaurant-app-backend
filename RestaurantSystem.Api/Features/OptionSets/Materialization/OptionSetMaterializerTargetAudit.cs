using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetMaterializerTargetAudit
{
    public static void Stamp(object row, string audit, DateTime now)
    {
        switch (row)
        {
            case ProductIngredient ingredient:
                ingredient.UpdatedAt = now;
                ingredient.UpdatedBy = audit;
                break;
            case ProductSideItem side:
                side.UpdatedAt = now;
                side.UpdatedBy = audit;
                break;
            case MenuSectionItem item:
                item.UpdatedAt = now;
                item.UpdatedBy = audit;
                break;
            case ProductCustomizationProductOption option:
                option.UpdatedAt = now;
                option.UpdatedBy = audit;
                break;
        }
    }
}
