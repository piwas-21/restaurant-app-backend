using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Utilities;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetProductByIdQuery;

// RequestedOrderType resolves the item's `Availability` for the guest's channel; null (no type
// chosen yet) reports it as orderable and still fills AllowedOrderTypes for the chip.
public record GetProductByIdQuery(Guid Id, OrderType? RequestedOrderType = null) : IQuery<ApiResponse<ProductDto>>;

public class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ApiResponse<ProductDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetProductByIdQueryHandler> _logger;
    private readonly string _baseUrl;

    public GetProductByIdQueryHandler(ApplicationDbContext context, ILogger<GetProductByIdQueryHandler> logger, IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _baseUrl = configuration["AWS:S3:BaseUrl"]!;
    }

    public async Task<ApiResponse<ProductDto>> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            // NOT for reaching soft-deleted products — the predicate below re-applies `!p.IsDeleted`,
            // so it does not. What it actually does is un-filter every INCLUDE, which is why deleted
            // categories reached the projection (§9.14) and why the side-item include below still
            // needs a filter of its own. Dropping it would silently change what this endpoint returns.
            .IgnoreQueryFilters()
            .AsSplitQuery()
            .Include(p => p.Descriptions)
            .Include(p => p.Images.Where(i => !i.IsDeleted).OrderBy(i => i.SortOrder))
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            // `!v.IsDeleted` is LOAD-BEARING, for the same reason the images include above carries
            // one: `IgnoreQueryFilters()` un-filters every INCLUDE, and `ProductVariation` is a
            // `SoftDeleteEntity`, so deleting a variation (which `UpdateProductCommand` does by
            // omitting it from the incoming list) left this endpoint serving it FOREVER. The list
            // endpoint filtered it and this one did not, so the admin editor re-fetched the row it
            // had just deleted and the guest sheet kept offering it — the §9.14 shape, on the one
            // include nobody had reached yet.
            .Include(p => p.Variations.Where(v => !v.IsDeleted).OrderBy(v => v.DisplayOrder))
                .ThenInclude(v => v.Descriptions)
            .Include(p => p.DetailedIngredients.Where(di => di.IsActive).OrderBy(di => di.DisplayOrder))
                .ThenInclude(di => di.Descriptions)
            .Include(p => p.DetailedIngredients)
                .ThenInclude(di => di.GlobalIngredient)
                    .ThenInclude(gi => gi!.Translations)
            .Include(p => p.SuggestedSideItems) // Add soft delete filter here
                .ThenInclude(si => si.SideItemProduct)
                    .ThenInclude(product => product.Images.Where(i => !i.IsDeleted).OrderBy(i => i.SortOrder))
            // #468: down to the option product's own recipe, which the shared bundle mapper
            // projects. Not optional — an unloaded collection is EMPTY, not absent, so leaving it
            // out serves a dish whose recipe reads as "no ingredients" rather than failing.
            // `AsSplitQuery()` above keeps it off the Cartesian product of the sibling includes.
            .Include(p => p.MenuDefinition!.Sections)
                .ThenInclude(s => s.Items)
                    .ThenInclude(i => i.Product.DetailedIngredients)
                        .ThenInclude(di => di.Descriptions)
            .FirstOrDefaultAsync(p => p.Id == query.Id && !p.IsDeleted, cancellationToken); // Also filter the main product
        if (product == null)
        {
            _logger.LogWarning("Product with ID {ProductId} not found", query.Id);
            return ApiResponse<ProductDto>.Failure("Product not found");
        }

        var productDto = new ProductDto
        {
            Availability = OrderTypeAvailability.Resolve(product, query.RequestedOrderType),
            AvailableOrderTypes = product.AvailableOrderTypes,
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            BasePrice = product.BasePrice,
            IsActive = product.IsActive,
            IsAvailable = product.IsAvailable,
            IsSpecial = product.IsSpecial,
            HideBaseProduct = product.HideBaseProduct,
            IsComponent = product.IsComponent,
            SauceMin = product.SauceMin,
            SauceMax = product.SauceMax,
            SauceIncludedFree = product.SauceIncludedFree,
            PreparationTimeMinutes = product.PreparationTimeMinutes,
            Type = product.Type,
            KitchenType = product.KitchenType,
            Ingredients = product.Ingredients,
            Allergens = product.Allergens,
            DisplayOrder = product.DisplayOrder,
            DetailedIngredients = product.DetailedIngredients
                .Select(di =>
                {
                    // Start with global translations if available
                    var content = new Dictionary<string, ProductIngredientContentDto>();

                    // `!IsDeleted` in code, not as a filtered include: EF Core's include filters
                    // apply to COLLECTIONS only, and this is a reference navigation. Without it the
                    // `IgnoreQueryFilters()` above serves a soft-deleted global ingredient's
                    // translations here — the §9.14 shape, newly reachable now that deleting a global
                    // ingredient soft-deletes instead of failing on its FK. The ingredient row itself
                    // still renders; only the global's translated names are withheld, and
                    // `ProductIngredient.Name` below is the fallback that already covers that.
                    if (di.GlobalIngredient != null && !di.GlobalIngredient.IsDeleted)
                    {
                        foreach (var trans in di.GlobalIngredient.Translations)
                        {
                            content[trans.LanguageCode] = new ProductIngredientContentDto
                            {
                                Name = trans.Name,
                                Description = null // Global ingredients don't have descriptions in this context yet
                            };
                        }
                    }

                    // Override with specific descriptions
                    foreach (var desc in di.Descriptions)
                    {
                        content[desc.LanguageCode] = new ProductIngredientContentDto
                        {
                            Name = desc.Name,
                            Description = desc.Description
                        };
                    }

                    return new ProductIngredientDto
                    {
                        Id = di.Id,
                        Name = di.Name,
                        IsOptional = di.IsOptional,
                        Price = di.Price,
                        IsIncludedInBasePrice = di.IsIncludedInBasePrice,
                        IsActive = di.IsActive,
                        DisplayOrder = di.DisplayOrder,
                        MaxQuantity = di.MaxQuantity,
                        GlobalIngredientId = di.GlobalIngredientId,
                        Kind = di.Kind,
                        ExclusionGroup = di.ExclusionGroup,
                        Content = content
                    };
                })
                .ToList(),
            Images = product.Images.Select(i => new ProductImageDto
            {
                Id = i.Id,
                Url = UrlJoin.Join(_baseUrl, i.Url),
                CardUrl = i.CardUrl is null ? null : UrlJoin.Join(_baseUrl, i.CardUrl),
                AltText = i.AltText,
                IsPrimary = i.IsPrimary,
                SortOrder = i.SortOrder,
                ProductId = i.ProductId
            }).ToList(),
            // `LiveProductCategories`, not `ProductCategories`: `IgnoreQueryFilters()` above
            // un-filters the INCLUDES, so without this a SOFT-DELETED category comes back here as a
            // live assignment while every other catalog surface reports none (§9.14).
            Categories = LiveProductCategories.Of(product)
                .OrderBy(pc => pc.DisplayOrder)
                .Select(pc => new ProductCategoryDto
                {
                    CategoryId = pc.CategoryId,
                    CategoryName = pc.Category.Name,
                    IsPrimary = pc.IsPrimary,
                    DisplayOrder = pc.DisplayOrder
                })
                .ToList(),
            PrimaryCategory = LiveProductCategories.Of(product)
                .Where(pc => pc.IsPrimary)
                .Select(pc => new CategoryDto
                {
                    Id = pc.Category.Id,
                    Name = pc.Category.Name,
                    Description = pc.Category.Description,
                    ImageUrl = pc.Category.ImageUrl,
                    IsActive = pc.Category.IsActive,
                    DisplayOrder = pc.Category.DisplayOrder,
                    IsHiddenFromAllTab = pc.Category.IsHiddenFromAllTab,
                    AvailableOrderTypes = pc.Category.AvailableOrderTypes
                })
                .FirstOrDefault(),
            Variations = product.Variations
                .Select(v => new ProductVariationDto
                {
                    Id = v.Id,
                    Name = v.Name,
                    Description = v.Description,
                    PriceModifier = v.PriceModifier,
                    FinalPrice = product.BasePrice + v.PriceModifier,
                    IsActive = v.IsActive,

                    DisplayOrder = v.DisplayOrder,
                    GlobalVariationId = v.GlobalVariationId,
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
            // `!IsDeleted` in code and not as a filtered include, for the same reason the global
            // ingredient above needs it: `SideItemProduct` is a REFERENCE navigation and EF Core's
            // include filters apply to collections only. This is the include whose comment used to
            // read "Add soft delete filter here" — measured on a live stack, deleting a product left
            // it offered as a side item on every product that suggested it, the same shape as the
            // variation defect this ships with.
            SuggestedSideItems = product.SuggestedSideItems
                .Where(si => si.SideItemProduct != null && !si.SideItemProduct.IsDeleted)
                .OrderBy(si => si.DisplayOrder)
                .Select(si => new SideItemDto
                {
                    Id = si.SideItemProduct.Id,
                    Name = si.SideItemProduct.Name,
                    Description = si.SideItemProduct.Description,
                    Price = si.SideItemProduct.BasePrice,
                    Type = si.SideItemProduct.Type,
                    IsRequired = si.IsRequired,
                    DisplayOrder = si.DisplayOrder,
                    Images = si.SideItemProduct.Images
                        .Select(i => new ProductImageDto
                        {
                            Id = i.Id,
                            Url = UrlJoin.Join(_baseUrl, i.Url),
                            CardUrl = i.CardUrl is null ? null : UrlJoin.Join(_baseUrl, i.CardUrl),
                            AltText = i.AltText,
                            IsPrimary = i.IsPrimary,
                            SortOrder = i.SortOrder,
                            ProductId = i.ProductId
                        })
                        .ToList()
                })
                .ToList(),
            // #468: the SAME projection `GET /api/Menus/{id}` uses. This read had one of its own
            // that carried an option row's id, name, price and display order and stopped there — no
            // recipe, no sauce rule, no allergens — so the guest sheet's by-id entry
            // (`useItemCustomizationSheet.openForProduct`) opened a bundle with nothing to
            // customize, and the mobile client reading this contract got the same. The two reads now
            // cannot drift, because there is only one of them.
            MenuDefinition = product.MenuDefinition != null
                ? MenuBundleMapper.MapDefinition(product.MenuDefinition)
                : null,
            Content = new()
        };

        foreach (var description in product.Descriptions)
        {
            productDto.Content[description.Lang] = new ProductDescriptionDto
            {
                Name = description.Name,
                Description = description.Description
            };
        }

        _logger.LogInformation("Retrieved product {ProductId} successfully", query.Id);
        return ApiResponse<ProductDto>.SuccessWithData(productDto);
    }
}
