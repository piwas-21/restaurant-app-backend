using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Categories.Queries.GetCategoryProductsQuery;

public record GetCategoryProductsQuery(
    Guid CategoryId,
    int PageNumber = 1,
    int PageSize = 10,
    bool? IsActive = null,
    bool? IsAvailable = null
) : IQuery<ApiResponse<PagedResult<CategoryProductDto>>>;


public class GetCategoryProductsQueryHandler : IQueryHandler<GetCategoryProductsQuery, ApiResponse<PagedResult<CategoryProductDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetCategoryProductsQueryHandler> _logger;

    public GetCategoryProductsQueryHandler(ApplicationDbContext context, ILogger<GetCategoryProductsQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<CategoryProductDto>>> Handle(GetCategoryProductsQuery query, CancellationToken cancellationToken)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == query.CategoryId && !c.IsDeleted, cancellationToken);

        if (category == null)
        {
            return ApiResponse<PagedResult<CategoryProductDto>>.Failure("Category not found");
        }

        var productsQuery = _context.ProductCategories
            .Include(pc => pc.Product)
                .ThenInclude(p => p.Images)
            .Include(pc => pc.Product)
                .ThenInclude(p => p.Variations)
                    .ThenInclude(v => v.Descriptions)
            .Where(pc => pc.CategoryId == query.CategoryId && !pc.Product.IsDeleted)
            .AsQueryable();

        if (query.IsActive.HasValue)
        {
            productsQuery = productsQuery.Where(pc => pc.Product.IsActive == query.IsActive.Value);
        }

        if (query.IsAvailable.HasValue)
        {
            productsQuery = productsQuery.Where(pc => pc.Product.IsAvailable == query.IsAvailable.Value);
        }

        var totalCount = await productsQuery.CountAsync(cancellationToken);

        var products = await productsQuery
            .OrderBy(pc => pc.DisplayOrder)
            .ThenBy(pc => pc.Product.DisplayOrder)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(pc => new CategoryProductDto
            {
                Id = pc.Product.Id,
                Name = pc.Product.Name,
                Description = pc.Product.Description,
                BasePrice = pc.Product.BasePrice,
                Images = pc.Product.Images.Select(i => new ProductImageDto
                {
                    Id = i.Id,
                    Url = i.Url,
                    IsPrimary = i.IsPrimary
                }).ToList(),
                IsAvailable = pc.Product.IsAvailable,
                IsPrimaryCategory = pc.IsPrimary,
                PreparationTimeMinutes = pc.Product.PreparationTimeMinutes,
                Variations = pc.Product.Variations
                    .OrderBy(v => v.DisplayOrder)
                    .Select(v => new ProductVariationDto
                    {
                        Id = v.Id,
                        Name = v.Name,
                        Description = v.Description,
                        PriceModifier = v.PriceModifier,
                        IsActive = v.IsActive,
                        DisplayOrder = v.DisplayOrder,
                        Content = v.Descriptions
                            .GroupBy(d => d.LanguageCode)
                            .Select(g => g.First())
                            .ToDictionary(
                                d => d.LanguageCode,
                                d => new ProductVariationContentDto
                                {
                                    Name = d.Name,
                                    Description = d.Description
                                }
                            )
                    }).ToList()
            })
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var result = new PagedResult<CategoryProductDto>(
            products,
            totalCount,
            query.PageNumber,
            query.PageSize,
            totalPages);

        _logger.LogInformation("Retrieved {Count} products for category {CategoryId}", products.Count, query.CategoryId);
        return ApiResponse<PagedResult<CategoryProductDto>>.SuccessWithData(result);
    }
}
