using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal sealed class CatalogueImportReuseKinds(
    IReadOnlyDictionary<Guid, IngredientKind> ingredients,
    IReadOnlyDictionary<Guid, OptionSetKind> optionSets)
{
    public static async Task<CatalogueImportReuseKinds> LoadAsync(
        ApplicationDbContext context,
        IEnumerable<CatalogueLocalEntityKey> references,
        CancellationToken cancellationToken)
    {
        var keys = references.ToArray();
        var ingredientIds = keys.Where(key => key.EntityType == "GlobalIngredient")
            .Select(key => key.Id).Distinct().ToArray();
        var optionSetIds = keys.Where(key => key.EntityType == "OptionSet")
            .Select(key => key.Id).Distinct().ToArray();
        var ingredients = ingredientIds.Length == 0
            ? new Dictionary<Guid, IngredientKind>()
            : await context.GlobalIngredients.AsNoTracking()
                .Where(row => ingredientIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.Kind, cancellationToken);
        var optionSets = optionSetIds.Length == 0
            ? new Dictionary<Guid, OptionSetKind>()
            : await context.OptionSets.AsNoTracking()
                .Where(row => optionSetIds.Contains(row.Id))
                .ToDictionaryAsync(row => row.Id, row => row.Kind, cancellationToken);
        return new CatalogueImportReuseKinds(ingredients, optionSets);
    }

    public bool IsCompatible(CentralCatalogueTemplateRevision revision, Guid localId)
    {
        if (revision.Type == "ingredient")
            return ingredients.TryGetValue(localId, out var kind) && MatchesIngredient(revision.Payload, kind);
        if (revision.Type == "option-set")
            return optionSets.TryGetValue(localId, out var kind) && MatchesOptionSet(revision.Payload, kind);
        return true;
    }

    internal static bool MatchesIngredient(JsonElement payload, IngredientKind kind) =>
        ReadKind(payload, "role") switch
        {
            "ingredient" => kind == IngredientKind.Ingredient,
            "sauce" => kind == IngredientKind.Sauce,
            _ => false
        };

    internal static bool MatchesOptionSet(JsonElement payload, OptionSetKind kind) =>
        ReadKind(payload, "kind") switch
        {
            "ingredient" => kind == OptionSetKind.Ingredient,
            "sauce" => kind == OptionSetKind.Sauce,
            "bundle-option" => kind == OptionSetKind.BundleChoice,
            "suggested-side" => kind == OptionSetKind.SuggestedSide,
            _ => false
        };

    private static string? ReadKind(JsonElement payload, string property) =>
        payload.ValueKind == JsonValueKind.Object &&
        payload.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
