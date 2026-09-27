using RestaurantSystem.Api.Features.OptionSets.Dtos;
using RestaurantSystem.Api.Features.OptionSets.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OptionSets.Materialization;

internal sealed record OptionSetMaterializerValidationContext(
    IReadOnlySet<Guid>? StagedProductIds,
    int MaximumEntryCount);

internal static class OptionSetMaterializerEntryValidation
{
    public static Task<IReadOnlyList<string?>> ValidateManyAsync(
        ApplicationDbContext context,
        OptionSetKind kind,
        IReadOnlyList<OptionSetEntry> entries,
        OptionSetMaterializerValidationContext validationContext,
        CancellationToken cancellationToken)
    {
        var dtos = entries.Select(ToDto).ToList();
        return OptionSetEntryValidator.ValidateManyAsync(
            context,
            kind,
            dtos,
            validationContext.MaximumEntryCount,
            stagedProductIds: validationContext.StagedProductIds,
            cancellationToken: cancellationToken);
    }

    private static OptionSetEntryDto ToDto(OptionSetEntry entry) => new()
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
    };
}
