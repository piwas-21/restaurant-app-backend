using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetProductsQuery;

public record GetProductsQuery(
    Guid? CategoryId,
    ProductType? Type,
    ProductType? ExcludeType,
    bool? IsActive,
    bool? IsAvailable,
    bool? isSpeacial,
    string? Search,
    int Page = 1,
    int PageSize = 20
) : IQuery<ApiResponse<PagedResult<ProductSummaryDto>>>;

public class GetProductsQueryHandler : IQueryHandler<GetProductsQuery, ApiResponse<PagedResult<ProductSummaryDto>>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetProductsQueryHandler> _logger;
    private readonly string _baseUrl;
    private readonly IConfiguration _configuration;

    public GetProductsQueryHandler(ApplicationDbContext context, ILogger<GetProductsQueryHandler> logger, IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
        _baseUrl = _configuration["AWS:S3:BaseUrl"]!;
    }

    public async Task<ApiResponse<PagedResult<ProductSummaryDto>>> Handle(GetProductsQuery query, CancellationToken cancellationToken)
    {
        var productsQuery = _context.Products
            .Include(p => p.Images)
            .Include(p => p.Descriptions)
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Include(p => p.Variations.Where(v => v.IsActive))
                .ThenInclude(v => v.Descriptions)
            .AsQueryable();

        // Apply filters
        if (query.CategoryId.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.ProductCategories.Any(pc => pc.CategoryId == query.CategoryId.Value));
        }

        if (query.Type.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.Type == query.Type.Value);
        }
        else
        {
            // Default behavior: Exclude Menu bundles unless specifically requested via Type
            productsQuery = productsQuery.Where(p => p.Type != ProductType.Menu);
        }

        if (query.ExcludeType.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.Type != query.ExcludeType.Value);
        }

        if (query.IsActive.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.IsActive == query.IsActive.Value);
        }

        if (query.IsAvailable.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.IsAvailable == query.IsAvailable.Value);
        }

        if (query.isSpeacial.HasValue)
        {
            productsQuery = productsQuery.Where(p => p.IsSpecial == query.isSpeacial.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchLower = query.Search.ToLower();

            productsQuery = productsQuery.Where(p => p.Name.ToLower().Contains(searchLower) || p.Descriptions.Any(c => c.Name.ToLower().Contains(searchLower)));
        }


        // Get total count
        var totalCount = await productsQuery.CountAsync(cancellationToken);


        var products = await productsQuery
        .OrderBy(p => p.DisplayOrder)
        .ThenBy(p => p.Name)
        .Skip((query.Page - 1) * query.PageSize)
        .Take(query.PageSize)
        .ToListAsync(cancellationToken);

        var productDtos = products.Select(p =>
        {
            var dto = new ProductSummaryDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                BasePrice = p.BasePrice,
                IsActive = p.IsActive,
                IsAvailable = p.IsAvailable,
                IsSpecial = p.IsSpecial,
                Type = p.Type,
                Allergens = p.Allergens,
                Ingredients = p.Ingredients,
                DetailedIngredients = new List<ProductIngredientDto>(),
                Images = p.Images.Select(s => new ProductImageDto
                {
                    Id = s.Id,
                    Url = _baseUrl + "/" + s.Url,
                    IsPrimary = s.IsPrimary,
                    SortOrder = s.SortOrder,
                    AltText = s.AltText
                }).ToList(),
                CategoryNames = p.ProductCategories.Select(pc => pc.Category.Name).ToList(),
                PrimaryCategoryName = p.ProductCategories
                    .Where(pc => pc.IsPrimary)
                    .Select(pc => pc.Category.Name)
                    .FirstOrDefault(),
                VariationCount = p.Variations.Count,
                Variations = p.Variations
                    .Where(v => v.IsActive)
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
                    })
                    .ToList(),
                SuggestedSideItems = new List<SideItemDto>(),
                Content = new() // Initialize Content dictionary
            };

            // Fill Content from Descriptions
            foreach (var description in p.Descriptions)
            {
                dto.Content[description.Lang] = new ProductDescriptionDto
                {
                    Name = description.Name,
                    Description = description.Description
                };
            }

            return dto;
        }).ToList();



        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var result = new PagedResult<ProductSummaryDto>(
            productDtos,
            totalCount,
            query.Page,
            query.PageSize,
            totalPages
        );

        _logger.LogInformation("Retrieved {ProductCount} products (page {Page} of {TotalPages})",
            products.Count, query.Page, totalPages);

        return ApiResponse<PagedResult<ProductSummaryDto>>.SuccessWithData(result,
            $"Retrieved {products.Count} products");
    }
}
