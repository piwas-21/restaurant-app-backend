using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

internal static class OptionSetProductEntryValidator
{
    public static Task<IReadOnlyList<string?>> ValidateManyAsync(
        ApplicationDbContext context,
        IReadOnlyList<OptionSetEntryDto> entries,
        OptionSetKind kind,
        OptionSetAuthoringSettings settings,
        bool requireActiveReference,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken) =>
        OptionSetReferenceBatchRules.ValidateProductsAsync(
            context, entries, kind, settings, requireActiveReference, stagedProductIds, cancellationToken);
}
