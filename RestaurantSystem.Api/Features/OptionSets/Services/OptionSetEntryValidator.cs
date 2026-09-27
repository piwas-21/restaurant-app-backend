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
        if (string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Trim().Length > 200 || entry.DisplayOrder < 0)
        {
            throw new BadRequestException("Each option needs a name up to 200 characters and a non-negative order");
        }

        if (kind is OptionSetKind.Ingredient or OptionSetKind.Sauce)
        {
            await OptionSetIngredientEntryValidator.ValidateAsync(
                context, kind, entry, requireActiveReference, cancellationToken);
            return;
        }

        await OptionSetProductEntryValidator.ValidateAsync(
            context, entry, kind, requireActiveReference, stagedProductIds, cancellationToken);
    }
}
