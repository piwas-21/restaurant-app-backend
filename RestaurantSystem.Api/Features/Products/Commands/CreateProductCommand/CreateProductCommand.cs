using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Categories.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.CreateProductCommand;

public record CreateProductCommand(
    string Name,
    string? Description,
    decimal BasePrice,
    bool IsActive,
    bool IsAvailable,
    bool IsSpecial,
    int PreparationTimeMinutes,
    ProductType Type,
    KitchenType KitchenType,
    List<string>? Ingredients,
    List<string>? Allergens,
    int DisplayOrder,
    List<Guid> CategoryIds,
    Guid? PrimaryCategoryId,
    List<CreateProductVariationDto>? Variations,
    List<Guid>? SuggestedSideItemIds,
    List<ProductIngredientDto>? DetailedIngredients,

    ProductDescriptionsDto Content
) : ICommand<ApiResponse<ProductDto>>;

public record CreateProductVariationDto(
    string Name,
    string? Description,
    decimal PriceModifier,
    bool IsActive,
    int DisplayOrder,
    Dictionary<string, ProductVariationContentDto>? Content
);

public class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, ApiResponse<ProductDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<CreateProductCommandHandler> _logger;

    public CreateProductCommandHandler(ApplicationDbContext context, ICurrentUserService currentUserService, ILogger<CreateProductCommandHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ApiResponse<ProductDto>> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var categories = await _context.Categories
               .Where(c => command.CategoryIds.Contains(c.Id))
               .ToListAsync(cancellationToken);

            // Validate primary category
            if (command.PrimaryCategoryId.HasValue && !command.CategoryIds.Contains(command.PrimaryCategoryId.Value))
            {
                return ApiResponse<ProductDto>.Failure("Primary category must be one of the selected categories");
            }

            if (command.SuggestedSideItemIds?.Any() == true)
            {
                var sideItemsExist = await _context.Products
                    .Where(p => command.SuggestedSideItemIds.Contains(p.Id))
                    .CountAsync(cancellationToken) == command.SuggestedSideItemIds.Count;

                if (!sideItemsExist)
                {
                    return ApiResponse<ProductDto>.Failure("One or more suggested side items not found or not side items");
                }
            }

            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = command.Name,
                Description = command.Description,
                BasePrice = command.BasePrice,
                IsActive = command.IsActive,
                IsSpecial = command.IsSpecial,
                IsAvailable = command.IsAvailable,
                PreparationTimeMinutes = command.PreparationTimeMinutes,
                Type = command.Type,
                KitchenType = command.KitchenType,
                Ingredients = command.Ingredients,
                Allergens = command.Allergens,
                DisplayOrder = command.DisplayOrder,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };

            _context.Products.Add(product);

            var displayOrder = 0;

            foreach (var categoryId in command.CategoryIds)
            {
                var productCategory = new ProductCategory
                {
                    CategoryId = categoryId,
                    IsPrimary = categoryId == command.PrimaryCategoryId,
                    DisplayOrder = displayOrder++,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUserService.GetAuditIdentifier()
                };
                _context.ProductCategories.Add(productCategory);
                product.ProductCategories.Add(productCategory);
            }

            var languageCodes = command.Content.Select(x => x.Key).ToList();
            var duplicateLanguageCodes = languageCodes.GroupBy(x => x)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateLanguageCodes.Any())
            {
                return ApiResponse<ProductDto>.Failure($"Duplicate language codes found: {string.Join(", ", duplicateLanguageCodes)}");
            }

            foreach (var (languageCode, description) in command.Content)
            {
                var productDescription = new ProductDescription
                {
                    Lang = languageCode,
                    Name = description.Name,
                    Description = description.Description,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUserService.GetAuditIdentifier()
                };
                _context.ProductDescriptions.Add(productDescription);
                product.Descriptions.Add(productDescription);
            }

            if (command.Variations?.Any() == true)
            {
                foreach (var variationDto in command.Variations)
                {
                    var variation = new ProductVariation
                    {
                        Name = variationDto.Name,
                        Description = variationDto.Description,
                        PriceModifier = variationDto.PriceModifier,
                        IsActive = variationDto.IsActive,
                        DisplayOrder = variationDto.DisplayOrder,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };
                    _context.ProductVariations.Add(variation);
                    product.Variations.Add(variation);

                    if (variationDto.Content != null)
                    {
                        foreach (var (languageCode, content) in variationDto.Content)
                        {
                            if (string.IsNullOrWhiteSpace(content.Name)) continue;

                            var description = new ProductVariationDescription
                            {
                                ProductVariation = variation,
                                LanguageCode = languageCode,
                                Name = content.Name,
                                Description = content.Description,
                                CreatedAt = DateTime.UtcNow,
                                CreatedBy = _currentUserService.GetAuditIdentifier()
                            };
                            _context.ProductVariationDescriptions.Add(description);
                            variation.Descriptions.Add(description);
                        }
                    }
                }
            }

            if (command.SuggestedSideItemIds?.Any() == true)
            {
                var sideItemDisplayOrder = 0;
                foreach (var sideItemId in command.SuggestedSideItemIds)
                {
                    var productSideItem = new ProductSideItem
                    {
                        MainProductId = product.Id,
                        SideItemProductId = sideItemId,
                        IsRequired = false,
                        DisplayOrder = sideItemDisplayOrder++,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };
                    _context.ProductSideItems.Add(productSideItem);
                    product.SuggestedSideItems.Add(productSideItem);
                }
            }

            // Add detailed ingredients
            if (command.DetailedIngredients?.Any() == true)
            {
                foreach (var ingredientDto in command.DetailedIngredients)
                {
                    var ingredient = new ProductIngredient
                    {
                        ProductId = product.Id,
                        Name = ingredientDto.Name,
                        IsOptional = ingredientDto.IsOptional,
                        Price = ingredientDto.Price,
                        IsIncludedInBasePrice = ingredientDto.IsIncludedInBasePrice,
                        IsActive = ingredientDto.IsActive,
                        DisplayOrder = ingredientDto.DisplayOrder,
                        MaxQuantity = ingredientDto.MaxQuantity,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };

                    _context.ProductIngredients.Add(ingredient);
                    product.DetailedIngredients.Add(ingredient);

                    // Add ingredient descriptions (only non-empty ones)
                    if (ingredientDto.Content != null)
                    {
                        foreach (var (languageCode, content) in ingredientDto.Content)
                        {
                            // Skip empty content entries
                            if (string.IsNullOrWhiteSpace(content.Name) && string.IsNullOrWhiteSpace(content.Description))
                            {
                                continue;
                            }

                            var description = new ProductIngredientDescription
                            {
                                ProductIngredient = ingredient,
                                LanguageCode = languageCode,
                                Name = content.Name,
                                Description = content.Description,
                                CreatedAt = DateTime.UtcNow,
                                CreatedBy = _currentUserService.GetAuditIdentifier()
                            };
                            _context.ProductIngredientDescriptions.Add(description);
                            ingredient.Descriptions.Add(description);
                        }
                    }
                }

            }

            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var createdProduct = await _context.Products
                .Include(p => p.ProductCategories)
                    .ThenInclude(pc => pc.Category)
                .Include(p => p.Variations)
                    .ThenInclude(v => v.Descriptions)
                .Include(p => p.SuggestedSideItems)
                    .ThenInclude(si => si.SideItemProduct)
                .Include(p => p.DetailedIngredients)
                    .ThenInclude(di => di.Descriptions)
                .Include(p => p.MenuDefinition)
                    .ThenInclude(md => md!.Sections)
                        .ThenInclude(s => s.Items)
                            .ThenInclude(i => i.Product)
                .FirstAsync(p => p.Id == product.Id, cancellationToken);

            var productDto = MapToProductDto(createdProduct);

            _logger.LogInformation("Product {ProductId} created successfully by user {UserId}",
                    product.Id, _currentUserService.UserId);

            return ApiResponse<ProductDto>.SuccessWithData(productDto, "Product created successfully");

        }
        catch
        {
            // Only rollback if the transaction is still active
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Transaction already completed or disposed, ignore rollback error
            }
            throw;
        }
    }

    private static ProductDto MapToProductDto(Product product)
    {
        var dto = new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            BasePrice = product.BasePrice,
            IsActive = product.IsActive,
            IsAvailable = product.IsAvailable,
            PreparationTimeMinutes = product.PreparationTimeMinutes,
            Type = product.Type,
            KitchenType = product.KitchenType,
            Ingredients = product.Ingredients,
            Allergens = product.Allergens,
            DisplayOrder = product.DisplayOrder,
            DetailedIngredients = product.DetailedIngredients.Select(di => new ProductIngredientDto
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
                    .Select(g => g.First()) // Take first if duplicates exist
                    .ToDictionary(
                        d => d.LanguageCode,
                        d => new ProductIngredientContentDto
                        {
                            Name = d.Name,
                            Description = d.Description
                        }
                    )
            }).ToList(),
            Categories = product.ProductCategories.Select(pc => new ProductCategoryDto
            {
                CategoryId = pc.CategoryId,
                CategoryName = pc.Category.Name,
                IsPrimary = pc.IsPrimary,
                DisplayOrder = pc.DisplayOrder
            }).ToList(),
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
            Variations = product.Variations.Select(v => new ProductVariationDto
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
            }).ToList(),
            SuggestedSideItems = product.SuggestedSideItems.Select(si => new SideItemDto
            {
                Id = si.SideItemProduct.Id,
                Name = si.SideItemProduct.Name,
                Description = si.SideItemProduct.Description,
                Price = si.SideItemProduct.BasePrice,
                IsRequired = si.IsRequired,
                DisplayOrder = si.DisplayOrder
            }).ToList(),
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
            dto.Content[description.Lang] = new ProductDescriptionDto
            {
                Name = description.Name,
                Description = description.Description
            };
        }
        return dto;
    }
}
