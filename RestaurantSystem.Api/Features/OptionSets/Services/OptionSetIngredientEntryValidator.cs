using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

internal static class OptionSetIngredientEntryValidator
{
    public static Task<IReadOnlyList<string?>> ValidateManyAsync(
        ApplicationDbContext context,
        OptionSetKind kind,
        IReadOnlyList<OptionSetEntryDto> entries,
        bool requireActiveReference,
        CancellationToken cancellationToken) =>
        OptionSetReferenceBatchRules.ValidateIngredientsAsync(
            context, kind, entries, requireActiveReference, cancellationToken);
}
