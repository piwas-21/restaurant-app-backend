using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Services;

internal static class OptionSetEntryValidator
{
    public static async Task ValidateAsync(
        ApplicationDbContext context,
        OptionSetKind kind,
        OptionSetEntryDto entry,
        bool requireActiveReference = true,
        IReadOnlySet<Guid>? stagedProductIds = null,
        CancellationToken cancellationToken = default)
    {
        var errors = await ValidateManyAsync(
            context, kind, [entry], requireActiveReference, stagedProductIds, cancellationToken);
        if (errors[0] is string error)
        {
            throw new BadRequestException(error);
        }
    }

    public static async Task<IReadOnlyList<string?>> ValidateManyAsync(
        ApplicationDbContext context,
        OptionSetKind kind,
        IReadOnlyList<OptionSetEntryDto> entries,
        bool requireActiveReference = true,
        IReadOnlySet<Guid>? stagedProductIds = null,
        CancellationToken cancellationToken = default)
    {
        var errors = entries.Select(entry =>
            string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Trim().Length > 200 || entry.DisplayOrder < 0
                ? "Each option needs a name up to 200 characters and a non-negative order"
                : null).ToArray();
        if (kind is OptionSetKind.Ingredient or OptionSetKind.Sauce)
        {
            var referenceErrors = await OptionSetIngredientEntryValidator.ValidateManyAsync(
                context, kind, entries, requireActiveReference, cancellationToken);
            CopyFirstErrors(errors, referenceErrors);
            return errors;
        }

        var productErrors = await OptionSetProductEntryValidator.ValidateManyAsync(
            context, entries, kind, requireActiveReference, stagedProductIds, cancellationToken);
        CopyFirstErrors(errors, productErrors);
        return errors;
    }

    private static void CopyFirstErrors(string?[] target, IReadOnlyList<string?> source)
    {
        for (var index = 0; index < target.Length; index++)
        {
            target[index] ??= source[index];
        }
    }
}
