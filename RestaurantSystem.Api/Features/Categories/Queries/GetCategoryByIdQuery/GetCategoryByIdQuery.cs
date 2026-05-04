using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Categories.Queries.GetCategoryByIdQuery;

public record GetCategoryByIdQuery(Guid Id) : IQuery<ApiResponse<CategoryDetailDto>>;

public class GetCategoryByIdQueryHandler : IQueryHandler<GetCategoryByIdQuery, ApiResponse<CategoryDetailDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetCategoryByIdQueryHandler> _logger;
    private readonly string _baseUrl;


    public GetCategoryByIdQueryHandler(ApplicationDbContext context, ILogger<GetCategoryByIdQueryHandler> logger, IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    }

    public async Task<ApiResponse<CategoryDetailDto>> Handle(GetCategoryByIdQuery query, CancellationToken cancellationToken)
    {
        var category = await _context.Categories
            .Include(c => c.ProductCategories)
                .ThenInclude(pc => pc.Product)
                    .ThenInclude(p => p.Images)
            .FirstOrDefaultAsync(c => c.Id == query.Id && !c.IsDeleted, cancellationToken);

        if (category == null)
        {
            _logger.LogWarning("Category with ID {CategoryId} not found", query.Id);
            return ApiResponse<CategoryDetailDto>.Failure("Category not found");
        }

        var featuredProducts = category.ProductCategories
            .Where(pc => !pc.Product.IsDeleted && pc.Product.IsActive && pc.Product.IsAvailable)
            .OrderBy(pc => pc.DisplayOrder)
            .Take(6)
            .Select(pc => new CategoryProductDto
            {
                Id = pc.Product.Id,
                Name = pc.Product.Name,
                Description = pc.Product.Description,
                BasePrice = pc.Product.BasePrice,
                Images = pc.Product.Images.Select(i => new ProductImageDto
                {
                    Id = i.Id,
                    Url = _baseUrl + "/" + i.Url,
                    AltText = i.AltText,
                    IsPrimary = i.IsPrimary,
                    SortOrder = i.SortOrder,
                    ProductId = i.ProductId
                }).ToList(),
                IsAvailable = pc.Product.IsAvailable,
                IsPrimaryCategory = pc.IsPrimary,
                PreparationTimeMinutes = pc.Product.PreparationTimeMinutes
            })
            .ToList();

        var categoryDto = new CategoryDetailDto
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ImageUrl = category.ImageUrl,
            IsActive = category.IsActive,
            DisplayOrder = category.DisplayOrder,
            ProductCount = category.ProductCategories.Count(pc => !pc.Product.IsDeleted && pc.Product.IsActive),
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt,
            FeaturedProducts = featuredProducts,
            Content = new Dictionary<string, CategoryContentDto>()
        };

        _logger.LogInformation("Retrieved category {CategoryId} successfully", query.Id);
        return ApiResponse<CategoryDetailDto>.SuccessWithData(categoryDto);
    }
}
