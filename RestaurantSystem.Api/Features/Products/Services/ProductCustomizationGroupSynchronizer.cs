using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Services;

internal static class ProductCustomizationGroupSynchronizer
{
    public static async Task SyncAsync(
        ApplicationDbContext context,
        Product product,
        IReadOnlyCollection<ProductCustomizationGroupDto> incoming,
        string auditIdentifier,
        CancellationToken cancellationToken)
    {
        ValidateShape(incoming);
        await ValidateReferencesAsync(context, product, incoming, cancellationToken);

        var incomingIds = incoming.Select(group => group.Id).OfType<Guid>().ToHashSet();
        context.ProductCustomizationGroups.RemoveRange(
            product.CustomizationGroups.Where(group => !incomingIds.Contains(group.Id)));

        foreach (var dto in incoming)
        {
            var group = ResolveGroup(product, dto, auditIdentifier);
            ApplyFields(group, dto, auditIdentifier);
            ReplaceChildren(context, group, dto, auditIdentifier);
        }
    }

    private static ProductCustomizationGroup ResolveGroup(
        Product product,
        ProductCustomizationGroupDto dto,
        string auditIdentifier)
    {
        if (!dto.Id.HasValue)
        {
            var created = new ProductCustomizationGroup
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier
            };
            product.CustomizationGroups.Add(created);
            return created;
        }

        return product.CustomizationGroups.FirstOrDefault(group => group.Id == dto.Id.Value)
            ?? throw new BadRequestException("A customization group does not belong to this product");
    }

    private static void ApplyFields(
        ProductCustomizationGroup group,
        ProductCustomizationGroupDto dto,
        string auditIdentifier)
    {
        group.Name = dto.Name.Trim();
        group.Description = dto.Description?.Trim();
        group.DisplayOrder = dto.DisplayOrder;
        group.IsRequired = dto.IsRequired;
        group.MinSelection = dto.MinSelection;
        group.MaxSelection = dto.MaxSelection;
        group.IncludedFreeUnits = dto.IncludedFreeUnits;
        group.IsActive = dto.IsActive;
        group.UpdatedAt = DateTime.UtcNow;
        group.UpdatedBy = auditIdentifier;
        group.CreatedBy ??= auditIdentifier;
    }

    private static void ReplaceChildren(
        ApplicationDbContext context,
        ProductCustomizationGroup group,
        ProductCustomizationGroupDto dto,
        string auditIdentifier)
    {
        context.ProductCustomizationGroupDescriptions.RemoveRange(group.Descriptions);
        context.ProductCustomizationIngredientOptions.RemoveRange(group.IngredientOptions);
        context.ProductCustomizationProductOptions.RemoveRange(group.ProductOptions);

        group.Descriptions = dto.Content.Select(entry => new ProductCustomizationGroupDescription
        {
            Id = Guid.NewGuid(),
            ProductCustomizationGroupId = group.Id,
            LanguageCode = entry.Key,
            Name = entry.Value.Name.Trim(),
            Description = entry.Value.Description?.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = auditIdentifier
        }).ToList();
        group.IngredientOptions = dto.IngredientOptions.Select(option => new ProductCustomizationIngredientOption
        {
            Id = Guid.NewGuid(),
            ProductCustomizationGroupId = group.Id,
            ProductIngredientId = option.ProductIngredientId,
            DisplayOrder = option.DisplayOrder,
            IsDefault = option.IsDefault,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = auditIdentifier
        }).ToList();
        group.ProductOptions = dto.ProductOptions.Select(option => new ProductCustomizationProductOption
        {
            Id = Guid.NewGuid(),
            ProductCustomizationGroupId = group.Id,
            OptionProductId = option.OptionProductId,
            AdditionalPrice = option.AdditionalPrice,
            DisplayOrder = option.DisplayOrder,
            IsDefault = option.IsDefault,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = auditIdentifier
        }).ToList();
    }

    private static void ValidateShape(IReadOnlyCollection<ProductCustomizationGroupDto> groups)
    {
        if (groups.Select(group => group.Id).OfType<Guid>().Distinct().Count()
            != groups.Count(group => group.Id.HasValue))
        {
            throw new BadRequestException("Duplicate customization group IDs are not allowed");
        }

        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Name) || group.Name.Trim().Length > 100
                || group.Description?.Trim().Length is > 500 || group.DisplayOrder < 0
                || group.MinSelection < 0 || group.MaxSelection < group.MinSelection
                || group.IncludedFreeUnits < 0 || group.IncludedFreeUnits > group.MaxSelection)
            {
                throw new BadRequestException("A customization group has invalid limits or metadata");
            }

            var optionCount = group.IngredientOptions.Count + group.ProductOptions.Count;
            if (group.MaxSelection > optionCount || (group.IsRequired && group.MinSelection == 0))
            {
                throw new BadRequestException("A customization group's limits do not match its options");
            }

            var productOptions = group.ProductOptions.Select(option => option.OptionProductId).ToList();
            if (productOptions.Distinct().Count() != productOptions.Count
                || group.ProductOptions.Any(option => option.AdditionalPrice < 0 || option.DisplayOrder < 0)
                || group.IngredientOptions.Any(option => option.DisplayOrder < 0)
                || group.Content.Any(entry => string.IsNullOrWhiteSpace(entry.Key)
                    || entry.Key.Length > 10 || string.IsNullOrWhiteSpace(entry.Value.Name)
                    || entry.Value.Name.Trim().Length > 100
                    || entry.Value.Description?.Trim().Length is > 500))
            {
                throw new BadRequestException("A customization group has invalid or duplicate options or translations");
            }
        }
    }

    private static async Task ValidateReferencesAsync(
        ApplicationDbContext context,
        Product product,
        IReadOnlyCollection<ProductCustomizationGroupDto> groups,
        CancellationToken cancellationToken)
    {
        var ingredientIds = groups.SelectMany(group => group.IngredientOptions)
            .Select(option => option.ProductIngredientId).ToList();
        if (ingredientIds.Distinct().Count() != ingredientIds.Count
            || ingredientIds.Any(id => product.DetailedIngredients.All(ingredient => ingredient.Id != id)))
        {
            throw new BadRequestException("Customization ingredients must be unique and belong to the product");
        }

        var productIds = groups.SelectMany(group => group.ProductOptions)
            .Select(option => option.OptionProductId).ToHashSet();
        if (productIds.Contains(product.Id))
        {
            throw new BadRequestException("A product cannot be its own customization option");
        }

        var validProductCount = await context.Products.CountAsync(
            option => productIds.Contains(option.Id) && !option.IsDeleted, cancellationToken);
        if (validProductCount != productIds.Count)
        {
            throw new BadRequestException("One or more customization product options do not exist");
        }
    }
}
