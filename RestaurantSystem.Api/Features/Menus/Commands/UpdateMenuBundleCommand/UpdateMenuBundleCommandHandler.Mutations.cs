using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Menus.Commands.UpdateMenuBundleCommand;

public partial class UpdateMenuBundleCommandHandler
{
    private async Task ValidateOfferParentAsync(
        Guid productId,
        MenuDefinitionDto menuDefinition,
        CancellationToken cancellationToken)
    {
        if (!menuDefinition.OfferParentSpecified)
        {
            return;
        }

        await MenuOfferLinkRules.EnsureValidAsync(
            _context,
            productId,
            menuDefinition.ParentOfferProductId,
            menuDefinition.ParentOfferVariationId,
            cancellationToken);
    }

    private async Task<bool> CategoriesExistAsync(
        List<Guid>? categoryIds,
        CancellationToken cancellationToken)
    {
        if (categoryIds?.Any() != true)
        {
            return true;
        }

        var count = await _context.Categories
            .CountAsync(category => categoryIds.Contains(category.Id), cancellationToken);
        return count == categoryIds.Count;
    }

    private void UpdateCategories(Product product, List<Guid>? categoryIds, Guid? primaryCategoryId)
    {
        if (categoryIds?.Any() != true)
        {
            return;
        }

        _context.ProductCategories.RemoveRange(product.ProductCategories);
        for (var index = 0; index < categoryIds.Count; index++)
        {
            _context.ProductCategories.Add(new ProductCategory
            {
                ProductId = product.Id,
                CategoryId = categoryIds[index],
                IsPrimary = categoryIds[index] == primaryCategoryId,
                DisplayOrder = index,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            });
        }
    }

    private void UpdateContent(Product product, ProductDescriptionsDto? content)
    {
        var contentMap = content ?? new ProductDescriptionsDto();
        if (contentMap.Any())
        {
            _context.ProductDescriptions.RemoveRange(product.Descriptions);
        }

        foreach (var (languageCode, description) in contentMap)
        {
            _context.ProductDescriptions.Add(new ProductDescription
            {
                ProductId = product.Id,
                Lang = languageCode,
                Name = description.Name,
                Description = description.Description,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier(),
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = _currentUserService.GetAuditIdentifier()
            });
        }
    }
}
