using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Categories.Queries.GetCategoriesQuery;

public record GetCategoriesQuery(
    int PageNumber = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    bool? IsActive = null,
    string? SortBy = null,
    bool IsDescending = false
) : IQuery<ApiResponse<PagedResult<CategoryDto>>>;

public class GetCategoriesQueryHandler : IQueryHandler<GetCategoriesQuery, ApiResponse<PagedResult<CategoryDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetCategoriesQueryHandler> _logger;

    public GetCategoriesQueryHandler(ApplicationDbContext context, ILogger<GetCategoriesQueryHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<CategoryDto>>> Handle(GetCategoriesQuery query, CancellationToken cancellationToken)
    {
        var categoriesQuery = _context.Categories
            .Where(c => !c.IsDeleted)
            .AsQueryable();

        // Apply filters
        if (query.IsActive.HasValue)
        {
            categoriesQuery = categoriesQuery.Where(c => c.IsActive == query.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            categoriesQuery = categoriesQuery.Where(c =>
                c.Name.Contains(query.SearchTerm) ||
                (c.Description != null && c.Description.Contains(query.SearchTerm)));
        }

        // Apply sorting
        categoriesQuery = query.SortBy?.ToLower() switch
        {
            "name" => query.IsDescending ? categoriesQuery.OrderByDescending(c => c.Name) : categoriesQuery.OrderBy(c => c.Name),
            "displayorder" => query.IsDescending ? categoriesQuery.OrderByDescending(c => c.DisplayOrder) : categoriesQuery.OrderBy(c => c.DisplayOrder),
            "productcount" => query.IsDescending ?
                categoriesQuery.OrderByDescending(c => c.ProductCategories.Count(pc => !pc.Product.IsDeleted)) :
                categoriesQuery.OrderBy(c => c.ProductCategories.Count(pc => !pc.Product.IsDeleted)),
            _ => categoriesQuery.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
        };

        var totalCount = await categoriesQuery.CountAsync(cancellationToken);

        var categories = await categoriesQuery
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                ImageUrl = c.ImageUrl,
                IsActive = c.IsActive,
                DisplayOrder = c.DisplayOrder,
                ProductCount = c.ProductCategories.Count(pc => !pc.Product.IsDeleted && pc.Product.IsActive),
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var result = new PagedResult<CategoryDto>(
            categories,
            totalCount,
            query.PageNumber,
            query.PageSize,
            totalPages);

        _logger.LogInformation("Retrieved {Count} categories", categories.Count);
        return ApiResponse<PagedResult<CategoryDto>>.SuccessWithData(result);
    }
}
