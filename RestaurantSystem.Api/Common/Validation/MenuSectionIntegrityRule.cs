using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Common.Validation;

/// <summary>Structural and tenant-reference checks shared by every menu section write path.</summary>
public static class MenuSectionIntegrityRule
{
    public static async Task ValidateAsync(
        ApplicationDbContext context,
        IEnumerable<MenuSectionDto> sections,
        CancellationToken cancellationToken)
    {
        var sectionList = sections.ToList();
        ValidateStructure(sectionList);
        await ValidateProductReferencesAsync(context, sectionList, cancellationToken);

        var references = sectionList.SelectMany(section => section.Items ?? [])
            .Where(item => item.ProductVariationId.HasValue)
            .Select(item => (item.ProductId, VariationId: item.ProductVariationId!.Value)).ToList();
        if (references.Count == 0)
        {
            return;
        }

        var variationIds = references.Select(reference => reference.VariationId).Distinct().ToList();
        var variations = await context.ProductVariations
            .Where(variation => variationIds.Contains(variation.Id) && !variation.IsDeleted)
            .Select(variation => new { variation.Id, variation.ProductId, variation.IsActive })
            .ToDictionaryAsync(variation => variation.Id, cancellationToken);
        foreach (var (productId, variationId) in references)
        {
            if (!variations.TryGetValue(variationId, out var variation))
            {
                throw new BadRequestException($"Menu section variation '{variationId}' was not found");
            }

            if (variation.ProductId != productId)
            {
                throw new BadRequestException(
                    $"Menu section variation '{variationId}' does not belong to product '{productId}'");
            }

            if (!variation.IsActive)
            {
                throw new BadRequestException($"Menu section variation '{variationId}' is not active");
            }
        }
    }

    public static List<MenuSectionDto> Project(IEnumerable<MenuSection> sections) => sections
        .Select(section => new MenuSectionDto
        {
            Id = section.Id,
            Name = section.Name,
            Description = section.Description,
            DisplayOrder = section.DisplayOrder,
            IsRequired = section.IsRequired,
            MinSelection = section.MinSelection,
            MaxSelection = section.MaxSelection,
            Items = section.Items.Select(item => new MenuSectionItemDto
            {
                Id = item.Id,
                ProductId = item.ProductId,
                ProductVariationId = item.ProductVariationId,
                AdditionalPrice = item.AdditionalPrice,
                DisplayOrder = item.DisplayOrder,
                IsDefault = item.IsDefault
            }).ToList()
        })
        .ToList();

    public static void ValidateStructure(IReadOnlyCollection<MenuSectionDto> sections)
    {
        var sectionIds = sections.Where(section => section.Id.HasValue)
            .Select(section => section.Id.GetValueOrDefault()).ToList();
        if (sectionIds.Distinct().Count() != sectionIds.Count)
        {
            throw new BadRequestException("Menu section IDs must be unique");
        }

        foreach (var section in sections)
        {
            var items = section.Items ?? [];
            var optionCount = items.Select(item => item.ProductId).Distinct().Count();
            var defaultCount = items.Count(item => item.IsDefault);
            if (items.Count != optionCount)
            {
                throw new BadRequestException($"Section '{section.Name}' cannot contain the same product more than once");
            }

            if (section.MinSelection < 0 || section.MaxSelection <= 0
                || section.MinSelection > section.MaxSelection || section.MaxSelection > optionCount)
            {
                throw new BadRequestException($"Section '{section.Name}' has invalid minimum or maximum selections");
            }

            if ((section.IsRequired && section.MinSelection == 0)
                || (!section.IsRequired && section.MinSelection != 0))
            {
                throw new BadRequestException($"Section '{section.Name}' has an invalid required-selection minimum");
            }

            if (defaultCount > section.MaxSelection || (defaultCount > 0 && defaultCount < section.MinSelection))
            {
                throw new BadRequestException($"Section '{section.Name}' has an invalid default selection count");
            }
        }
    }

    public static async Task ValidateProductReferencesAsync(
        ApplicationDbContext context,
        IEnumerable<MenuSectionDto> sections,
        CancellationToken cancellationToken)
    {
        var productIds = sections.SelectMany(section => section.Items ?? [])
            .Select(item => item.ProductId).Distinct().ToList();
        if (productIds.Count == 0)
        {
            return;
        }

        var foundProductIds = await context.Products.Where(product => productIds.Contains(product.Id))
            .Select(product => product.Id).ToListAsync(cancellationToken);
        if (foundProductIds.Count != productIds.Count)
        {
            throw new BadRequestException("One or more menu options reference a missing or deleted product");
        }
    }
}
