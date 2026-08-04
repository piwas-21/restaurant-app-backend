using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetFeaturedSpecialQuery;

/// <summary>
/// Query to get the currently featured special product
/// </summary>
/// <param name="RequestedOrderType">
/// The channel the guest is browsing on, or <c>null</c> when they have not chosen one. Drives
/// <see cref="FeaturedSpecialDto.Availability"/> exactly as it does on the catalog queries.
/// </param>
public record GetFeaturedSpecialQuery(OrderType? RequestedOrderType = null)
    : IQuery<ApiResponse<FeaturedSpecialDto?>>;

public class GetFeaturedSpecialQueryHandler : IQueryHandler<GetFeaturedSpecialQuery, ApiResponse<FeaturedSpecialDto?>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetFeaturedSpecialQueryHandler> _logger;
    private readonly string _baseUrl;

    public GetFeaturedSpecialQueryHandler(
        ApplicationDbContext context,
        ILogger<GetFeaturedSpecialQueryHandler> logger,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    }

    public async Task<ApiResponse<FeaturedSpecialDto?>> Handle(
        GetFeaturedSpecialQuery query,
        CancellationToken cancellationToken)
    {
        // Get the product where IsFeaturedSpecial = true
        var featuredProduct = await _context.Products
            // ProductCategories -> Category is load-bearing, not cosmetic: `OrderTypeAvailability`
            // resolves an inheriting product through its PRIMARY category, and an unloaded
            // collection reads as UNRESTRICTED. Omit this include and the banner reports every
            // restricted special as orderable, silently — no exception, no empty field.
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Include(p => p.Images)
            .Include(p => p.Variations)
            .Include(p => p.SuggestedSideItems)
                .ThenInclude(si => si.SideItemProduct)
                .ThenInclude(s => s.Images)
            .Include(p => p.DetailedIngredients)
                .ThenInclude(di => di.Descriptions)
            .Where(p => p.IsFeaturedSpecial && p.IsSpecial && p.IsActive)
            // Five sibling collections LEFT-JOIN into each other's cartesian product, and this
            // endpoint is public + uncached + hit on every menu page load. `GetProductByIdQuery`
            // splits a near-identical graph for the same reason.
            .AsSplitQuery()
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
                .Where(img => img.IsPrimary && !string.IsNullOrEmpty(img.Url))
                .Select(img => UrlJoin.Join(_baseUrl, img.Url))
                .FirstOrDefault() ?? featuredProduct.ImageUrl,
            Availability = OrderTypeAvailability.Resolve(featuredProduct, query.RequestedOrderType),
            // A combo can be the featured item — nothing in SetFeaturedSpecialCommand prevents it —
            // and a client cannot tell one from a plain product without this.
            Type = featuredProduct.Type,
            FeaturedDate = featuredProduct.FeaturedDate ?? DateTime.UtcNow,
            PreparationTimeMinutes = featuredProduct.PreparationTimeMinutes,
            Ingredients = featuredProduct.Ingredients,
            Allergens = featuredProduct.Allergens,
            Images = featuredProduct.Images.Select(img => new ProductImageDto
            {
                Id = img.Id,
                Url = UrlJoin.Join(_baseUrl, img.Url),
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
                        .Where(img => img.IsPrimary && !string.IsNullOrEmpty(img.Url))
                        .Select(img => UrlJoin.Join(_baseUrl, img.Url))
                        .FirstOrDefault() ?? si.SideItemProduct.ImageUrl,
                    IsRequired = si.IsRequired,
                    DisplayOrder = si.DisplayOrder,
                    Images = si.SideItemProduct.Images.Select(img => new ProductImageDto
                    {
                        Id = img.Id,
                        Url = UrlJoin.Join(_baseUrl, img.Url),
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
