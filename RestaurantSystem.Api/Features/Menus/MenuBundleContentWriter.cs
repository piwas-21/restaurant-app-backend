using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Menus;

internal static class MenuBundleContentWriter
{
    public static void Add(
        ApplicationDbContext context,
        Product product,
        ProductDescriptionsDto content,
        string actor)
    {
        foreach (var (languageCode, description) in content)
        {
            var row = new ProductDescription
            {
                Lang = languageCode,
                Name = description.Name,
                Description = description.Description,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = actor
            };
            context.ProductDescriptions.Add(row);
            product.Descriptions.Add(row);
        }
    }
}
