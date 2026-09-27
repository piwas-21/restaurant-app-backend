using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace RestaurantSystem.Api.Features.Products.Services;

internal static class ProductCustomizationGroupOptionSetGuard
{
    public static async Task<HashSet<Guid>> ValidateAndGetProtectedIdsAsync(
        ApplicationDbContext context,
        Product product,
        IReadOnlyCollection<ProductCustomizationGroupDto> incoming,
        CancellationToken cancellationToken)
    {
        var protectedIds = await context.OptionSetAttachments
            .Where(attachment => attachment.Role == OptionSetAttachmentRole.ProductChoice
                && attachment.TargetProductId == product.Id
                && attachment.TargetCustomizationGroupId != null)
            .Select(attachment => attachment.TargetCustomizationGroupId!.Value)
            .ToHashSetAsync(cancellationToken);
        if (protectedIds.Count == 0)
        {
            return protectedIds;
        }

        foreach (var groupId in protectedIds)
        {
            var group = product.CustomizationGroups.SingleOrDefault(candidate => candidate.Id == groupId)
                ?? throw new ConflictException("An attached option-set group is missing. Detach the set before replacing product choices.");
            var dto = incoming.SingleOrDefault(candidate => candidate.Id == groupId);
            if (dto is null || dto.AuthoringVersion != group.AuthoringVersion || !Matches(group, dto))
            {
                throw new ConflictException(
                    "This product choice group is managed by an option set. Use its versioned option-set preview/apply workflow before changing or removing it.");
            }
        }

        return protectedIds;
    }

    private static bool Matches(ProductCustomizationGroup group, ProductCustomizationGroupDto dto)
    {
        return group.Name == dto.Name.Trim()
            && group.Description == dto.Description?.Trim()
            && group.DisplayOrder == dto.DisplayOrder
            && group.IsRequired == dto.IsRequired
            && group.MinSelection == dto.MinSelection
            && group.MaxSelection == dto.MaxSelection
            && group.IncludedFreeUnits == dto.IncludedFreeUnits
            && group.IsActive == dto.IsActive
            && SameContent(group.Descriptions, dto.Content)
            && SameIngredients(group.IngredientOptions, dto.IngredientOptions)
            && SameProducts(group.ProductOptions, dto.ProductOptions);
    }

    private static bool SameContent(
        IEnumerable<ProductCustomizationGroupDescription> saved,
        Dictionary<string, ProductCustomizationGroupContentDto> incoming)
    {
        var rows = saved.ToDictionary(row => row.LanguageCode, StringComparer.OrdinalIgnoreCase);
        return rows.Count == incoming.Count && incoming.All(pair => rows.TryGetValue(pair.Key, out var row)
            && row.Name == pair.Value.Name.Trim()
            && row.Description == pair.Value.Description?.Trim());
    }

    private static bool SameIngredients(
        IEnumerable<ProductCustomizationIngredientOption> saved,
        List<ProductCustomizationIngredientOptionDto> incoming)
    {
        var rows = saved.ToDictionary(row => row.Id);
        return rows.Count == incoming.Count && incoming.All(option => option.Id is Guid id
            && rows.TryGetValue(id, out var row)
            && row.ProductIngredientId == option.ProductIngredientId
            && row.DisplayOrder == option.DisplayOrder
            && row.IsDefault == option.IsDefault);
    }

    private static bool SameProducts(
        IEnumerable<ProductCustomizationProductOption> saved,
        List<ProductCustomizationProductOptionDto> incoming)
    {
        var rows = saved.ToDictionary(row => row.Id);
        return rows.Count == incoming.Count && incoming.All(option => option.Id is Guid id
            && rows.TryGetValue(id, out var row)
            && row.OptionProductId == option.OptionProductId
            && row.AdditionalPrice == option.AdditionalPrice
            && row.DisplayOrder == option.DisplayOrder
            && row.IsDefault == option.IsDefault);
    }
}
