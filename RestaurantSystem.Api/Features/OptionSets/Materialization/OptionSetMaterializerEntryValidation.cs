using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Api.Features.OptionSets.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal static class OptionSetMaterializerEntryValidation
{
    public static Task ValidateAsync(
        ApplicationDbContext context,
        OptionSetKind kind,
        OptionSetEntry entry,
        IReadOnlySet<Guid>? stagedProductIds,
        CancellationToken cancellationToken) =>
        OptionSetEntryValidator.ValidateAsync(context, kind, new OptionSetEntryDto
        {
            Id = entry.Id,
            Name = entry.Name,
            DisplayOrder = entry.DisplayOrder,
            GlobalIngredientId = entry.GlobalIngredientId,
            ProductId = entry.ProductId,
            ProductVariationId = entry.ProductVariationId,
            IsOptional = entry.IsOptional,
            MaxQuantity = entry.MaxQuantity,
            Price = entry.Price,
            IsIncludedInBasePrice = entry.IsIncludedInBasePrice,
            IsRequired = entry.IsRequired,
            AdditionalPrice = entry.AdditionalPrice,
            IsDefault = entry.IsDefault
        }, stagedProductIds: stagedProductIds, cancellationToken: cancellationToken);
}
