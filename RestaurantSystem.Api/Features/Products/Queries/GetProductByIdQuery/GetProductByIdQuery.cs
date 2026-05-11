using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Queries.GetProductByIdQuery;

public record GetProductByIdQuery(Guid Id) : IQuery<ApiResponse<ProductDto>>;

public class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ApiResponse<ProductDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GetProductByIdQueryHandler> _logger;
    private readonly IConfiguration _configuration;

    public GetProductByIdQueryHandler(ApplicationDbContext context, ILogger<GetProductByIdQueryHandler> logger, IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<ApiResponse<ProductDto>> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["AWS:S3:BaseUrl"]!;
        var product = await _context.Products
            .IgnoreQueryFilters() // This will load ALL products, including soft-deleted ones
            .AsSplitQuery()
            .Include(p => p.Descriptions)
            .Include(p => p.Images.Where(i => !i.IsDeleted).OrderBy(i => i.SortOrder))
            .Include(p => p.ProductCategories)
                .ThenInclude(pc => pc.Category)
            .Include(p => p.Variations.OrderBy(v => v.DisplayOrder))
                .ThenInclude(v => v.Descriptions)
            .Include(p => p.DetailedIngredients.Where(di => di.IsActive).OrderBy(di => di.DisplayOrder))
                .ThenInclude(di => di.Descriptions)
            .Include(p => p.DetailedIngredients)
                .ThenInclude(di => di.GlobalIngredient)
                    .ThenInclude(gi => gi!.Translations)
            .Include(p => p.SuggestedSideItems) // Add soft delete filter here
                .ThenInclude(si => si.SideItemProduct)
                    .ThenInclude(product => product.Images.Where(i => !i.IsDeleted).OrderBy(i => i.SortOrder))
            .Include(p => p.MenuDefinition)
                .ThenInclude(md => md!.Sections)
                    .ThenInclude(s => s.Items)
                        .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(p => p.Id == query.Id && !p.IsDeleted, cancellationToken); // Also filter the main product
        if (product == null)
        {
            _logger.LogWarning("Product with ID {ProductId} not found", query.Id);
            return ApiResponse<ProductDto>.Failure("Product not found");
        }

        var productDto = new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            BasePrice = product.BasePrice,
            IsActive = product.IsActive,
            IsAvailable = product.IsAvailable,
            IsSpecial = product.IsSpecial,
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

                    if (di.GlobalIngredient != null)
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
                        Content = content
                    };
                })
                .ToList(),
            Images = product.Images.Select(i => new ProductImageDto
            {
                Id = i.Id,
                Url = baseUrl + "/" + i.Url,
                AltText = i.AltText,
                IsPrimary = i.IsPrimary,
                SortOrder = i.SortOrder,
                ProductId = i.ProductId
            }).ToList(),
            Categories = product.ProductCategories
                .OrderBy(pc => pc.DisplayOrder)
                .Select(pc => new ProductCategoryDto
                {
                    CategoryId = pc.CategoryId,
                    CategoryName = pc.Category.Name,
                    IsPrimary = pc.IsPrimary,
                    DisplayOrder = pc.DisplayOrder
                })
                .ToList(),
            PrimaryCategory = product.ProductCategories
                .Where(pc => pc.IsPrimary)
                .Select(pc => new CategoryDto
                {
                    Id = pc.Category.Id,
                    Name = pc.Category.Name,
                    Description = pc.Category.Description,
                    ImageUrl = pc.Category.ImageUrl,
                    IsActive = pc.Category.IsActive,
                    DisplayOrder = pc.Category.DisplayOrder
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
            SuggestedSideItems = product.SuggestedSideItems
                .Where(si => si.SideItemProduct != null) // Add this
                .OrderBy(si => si.DisplayOrder)
                .Select(si => new SideItemDto
                {
                    Id = si.SideItemProduct.Id,
                    Name = si.SideItemProduct.Name,
                    Description = si.SideItemProduct.Description,
                    Price = si.SideItemProduct.BasePrice,
                    IsRequired = si.IsRequired,
                    DisplayOrder = si.DisplayOrder,
                    Images = si.SideItemProduct.Images
                        .Select(i => new ProductImageDto
                        {
                            Id = i.Id,
                            Url = baseUrl + "/" + i.Url,
                            AltText = i.AltText,
                            IsPrimary = i.IsPrimary,
                            SortOrder = i.SortOrder,
                            ProductId = i.ProductId
                        })
                        .ToList()
                })
                .ToList(),
            MenuDefinition = product.MenuDefinition != null ? new MenuDefinitionDto
            {
                Id = product.MenuDefinition.Id,
                IsAlwaysAvailable = product.MenuDefinition.IsAlwaysAvailable,
                StartTime = product.MenuDefinition.StartTime,
                EndTime = product.MenuDefinition.EndTime,
                AvailableMonday = product.MenuDefinition.AvailableMonday,
                AvailableTuesday = product.MenuDefinition.AvailableTuesday,
                AvailableWednesday = product.MenuDefinition.AvailableWednesday,
                AvailableThursday = product.MenuDefinition.AvailableThursday,
                AvailableFriday = product.MenuDefinition.AvailableFriday,
                AvailableSaturday = product.MenuDefinition.AvailableSaturday,
                AvailableSunday = product.MenuDefinition.AvailableSunday,
                Sections = product.MenuDefinition.Sections.Select(s => new MenuSectionDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    Description = s.Description,
                    DisplayOrder = s.DisplayOrder,
                    IsRequired = s.IsRequired,
                    MinSelection = s.MinSelection,
                    MaxSelection = s.MaxSelection,
                    Items = s.Items.Select(i => new MenuSectionItemDto
                    {
                        Id = i.Id,
                        ProductId = i.ProductId,
                        ProductName = i.Product?.Name,
                        AdditionalPrice = i.AdditionalPrice,
                        DisplayOrder = i.DisplayOrder,
                        IsDefault = i.IsDefault
                    }).OrderBy(i => i.DisplayOrder).ToList()
                }).OrderBy(s => s.DisplayOrder).ToList()
            } : null,
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
