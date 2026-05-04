using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetFeaturedSpecialQuery;

/// <summary>
/// Query to get the currently featured special product
/// </summary>
public record GetFeaturedSpecialQuery() : IQuery<ApiResponse<FeaturedSpecialDto?>>;

public class GetFeaturedSpecialQueryHandler : IQueryHandler<GetFeaturedSpecialQuery, ApiResponse<FeaturedSpecialDto?>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetFeaturedSpecialQueryHandler> _logger;
    private readonly string _baseUrl;
    private readonly IConfiguration _configuration;

    public GetFeaturedSpecialQueryHandler(
        ApplicationDbContext context,
        ILogger<GetFeaturedSpecialQueryHandler> logger,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
        _baseUrl = _configuration["AWS:S3:BaseUrl"]!;
    }

    public async Task<ApiResponse<FeaturedSpecialDto?>> Handle(
        GetFeaturedSpecialQuery query,
        CancellationToken cancellationToken)
    {
        // Get the product where IsFeaturedSpecial = true
        var featuredProduct = await _context.Products
            .Include(p => p.Images)
            .Include(p => p.Variations)
            .Include(p => p.SuggestedSideItems)
                .ThenInclude(si => si.SideItemProduct)
                .ThenInclude(s => s.Images)
            .Include(p => p.DetailedIngredients)
                .ThenInclude(di => di.Descriptions)
            .Where(p => p.IsFeaturedSpecial && p.IsSpecial && p.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (featuredProduct == null)
        {
            _logger.LogInformation("No featured special found");
            return ApiResponse<FeaturedSpecialDto?>.SuccessWithData(null, "No featured special available");
        }

        // Map to DTO
        var featuredSpecialDto = new FeaturedSpecialDto
        {
            Id = featuredProduct.Id,
            Name = featuredProduct.Name,
            Description = featuredProduct.Description,
            BasePrice = featuredProduct.BasePrice,
            ImageUrl = featuredProduct.Images
                .Where(img => img.IsPrimary)
                .Select(img => _baseUrl + "/" + img.Url)
                .FirstOrDefault() ?? featuredProduct.ImageUrl,
            FeaturedDate = featuredProduct.FeaturedDate ?? DateTime.UtcNow,
            PreparationTimeMinutes = featuredProduct.PreparationTimeMinutes,
            Ingredients = featuredProduct.Ingredients,
            Allergens = featuredProduct.Allergens,
            Images = featuredProduct.Images.Select(img => new ProductImageDto
            {
                Id = img.Id,
                Url = _baseUrl + "/" + img.Url,
                IsPrimary = img.IsPrimary,
                SortOrder = img.SortOrder,
                AltText = img.AltText
            }).ToList(),
            Variations = featuredProduct.Variations
                .OrderBy(v => v.DisplayOrder)
                .Select(v => new ProductVariationDto
                {
                    Id = v.Id,
                    Name = v.Name,
                    Description = v.Description,
                    PriceModifier = v.PriceModifier,
                    FinalPrice = featuredProduct.BasePrice + v.PriceModifier,
                    IsActive = v.IsActive,
                    DisplayOrder = v.DisplayOrder
                }).ToList(),
            SuggestedSideItems = featuredProduct.SuggestedSideItems
                .Where(si => si.SideItemProduct.IsActive)
                .OrderBy(si => si.DisplayOrder)
                .Select(si => new SideItemDto
                {
                    Id = si.SideItemProduct.Id,
                    Name = si.SideItemProduct.Name,
                    Description = si.SideItemProduct.Description,
                    Price = si.SideItemProduct.BasePrice,
                    ImageUrl = si.SideItemProduct.Images
                        .Where(img => img.IsPrimary)
                        .Select(img => _baseUrl + "/" + img.Url)
                        .FirstOrDefault() ?? si.SideItemProduct.ImageUrl,
                    IsRequired = si.IsRequired,
                    DisplayOrder = si.DisplayOrder,
                    Images = si.SideItemProduct.Images.Select(img => new ProductImageDto
                    {
                        Id = img.Id,
                        Url = _baseUrl + "/" + img.Url,
                        IsPrimary = img.IsPrimary,
                        SortOrder = img.SortOrder,
                        AltText = img.AltText
                    }).ToList()
                }).ToList(),
            DetailedIngredients = featuredProduct.DetailedIngredients
                .Where(di => di.IsActive)
                .OrderBy(di => di.DisplayOrder)
                .Select(di => new ProductIngredientDto
                {
                    Id = di.Id,
                    Name = di.Name,
                    IsOptional = di.IsOptional,
                    Price = di.Price,
                    IsIncludedInBasePrice = di.IsIncludedInBasePrice,
                    IsActive = di.IsActive,
                    DisplayOrder = di.DisplayOrder,
                    MaxQuantity = di.MaxQuantity,
                    Content = di.Descriptions
                        .GroupBy(d => d.LanguageCode)
                        .ToDictionary(
                            g => g.Key,
                            g => new ProductIngredientContentDto
                            {
                                Name = g.First().Name,
                                Description = g.First().Description
                            })
                }).ToList()
        };

        _logger.LogInformation(
            "Retrieved featured special: {ProductName} (ID: {ProductId})",
            featuredProduct.Name, featuredProduct.Id);

        return ApiResponse<FeaturedSpecialDto?>.SuccessWithData(
            featuredSpecialDto,
            "Featured special retrieved successfully");
    }
}
