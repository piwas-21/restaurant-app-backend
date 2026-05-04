using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetSpecialProductsQuery;

/// <summary>
/// Query to get all products marked as special (IsSpecial = true)
/// </summary>
public record GetSpecialProductsQuery(
    int Page = 1,
    int PageSize = 20
) : IQuery<ApiResponse<PagedResult<SpecialProductDto>>>;

public class GetSpecialProductsQueryHandler : IQueryHandler<GetSpecialProductsQuery, ApiResponse<PagedResult<SpecialProductDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetSpecialProductsQueryHandler> _logger;
    private readonly string _baseUrl;
    private readonly IConfiguration _configuration;

    public GetSpecialProductsQueryHandler(
        ApplicationDbContext context,
        ILogger<GetSpecialProductsQueryHandler> logger,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
        _baseUrl = _configuration["AWS:S3:BaseUrl"]!;
    }

    public async Task<ApiResponse<PagedResult<SpecialProductDto>>> Handle(
        GetSpecialProductsQuery query,
        CancellationToken cancellationToken)
    {
        // Query all products where IsSpecial = true
        var specialProductsQuery = _context.Products
            .Include(p => p.Images)
            .Where(p => p.IsSpecial)
            .AsQueryable();

        // Get total count
        var totalCount = await specialProductsQuery.CountAsync(cancellationToken);

        // Get paginated products
        var products = await specialProductsQuery
            .OrderByDescending(p => p.IsFeaturedSpecial) // Featured first
            .ThenBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        // Map to DTOs
        var productDtos = products.Select(p => new SpecialProductDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            BasePrice = p.BasePrice,
            ImageUrl = p.Images
                .Where(img => img.IsPrimary)
                .Select(img => _baseUrl + "/" + img.Url)
                .FirstOrDefault() ?? p.ImageUrl,
            IsActive = p.IsActive,
            IsAvailable = p.IsAvailable,
            IsSpecial = p.IsSpecial,
            IsFeaturedSpecial = p.IsFeaturedSpecial,
            FeaturedDate = p.FeaturedDate,
            DisplayOrder = p.DisplayOrder
        }).ToList();

        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var result = new PagedResult<SpecialProductDto>(
            productDtos,
            totalCount,
            query.Page,
            query.PageSize,
            totalPages
        );

        _logger.LogInformation(
            "Retrieved {ProductCount} special products (page {Page} of {TotalPages})",
            products.Count, query.Page, totalPages);

        return ApiResponse<PagedResult<SpecialProductDto>>.SuccessWithData(
            result,
            $"Retrieved {products.Count} special products");
    }
}
