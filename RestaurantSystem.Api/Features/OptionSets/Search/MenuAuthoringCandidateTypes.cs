namespace RestaurantSystem.Api.Features.OptionSets.Search;

internal static class MenuAuthoringCandidateTypes
{
    public const int ProductRank = 0;
    public const int ComponentRank = 1;
    public const int BundleRank = 2;
    public const int IngredientRank = 3;
    public const int OptionSetRank = 4;
    public const string Product = "product";
    public const string Component = "component";
    public const string Bundle = "bundle";
    public const string Ingredient = "ingredient";
    public const string OptionSet = "optionSet";

    public static int Rank(string type) => type switch
    {
        Product => ProductRank,
        Component => ComponentRank,
        Bundle => BundleRank,
        Ingredient => IngredientRank,
        OptionSet => OptionSetRank,
        _ => int.MaxValue
    };

    public static bool IsKnown(string type) => Rank(type) != int.MaxValue;
}
