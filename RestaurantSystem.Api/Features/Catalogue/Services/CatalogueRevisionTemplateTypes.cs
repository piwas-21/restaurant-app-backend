namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static class CatalogueRevisionTemplateTypes
{
    public static string? ForEntity(string localEntityType) => localEntityType switch
    {
        "Category" => "category",
        "GlobalIngredient" => "ingredient",
        "Product" => "item",
        "MenuBundle" => "bundle",
        "OptionSet" => "option-set",
        _ => null
    };
}
