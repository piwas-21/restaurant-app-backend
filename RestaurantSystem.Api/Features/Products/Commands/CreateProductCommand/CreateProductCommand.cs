using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalog;
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

    ProductDescriptionsDto Content,
    // OrderChannels bitmask; null = INHERIT from the primary category. Optional so existing
    // clients keep working (they inherit, which is the pre-feature behaviour).
    int? AvailableOrderTypes = null
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
                AvailableOrderTypes = command.AvailableOrderTypes,
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

            var productDto = ProductDtoMapper.MapToProductDto(createdProduct);

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
}
