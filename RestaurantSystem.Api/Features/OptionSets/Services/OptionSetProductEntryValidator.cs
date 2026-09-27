using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

internal static class OptionSetProductEntryValidator
{
    public static Task<IReadOnlyList<string?>> ValidateManyAsync(
        ApplicationDbContext context,
        IReadOnlyList<OptionSetEntryDto> entries,
        OptionSetKind kind,
        int maximumEntryCount,
        bool requireActiveReference,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken) =>
        OptionSetReferenceBatchRules.ValidateProductsAsync(
            context, entries, kind, maximumEntryCount, requireActiveReference, stagedProductIds, cancellationToken);
}
