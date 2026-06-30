using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Queries.GetProductByIdQuery;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Products.Commands.UpdateProductCommand;

public record UpdateProductCommand(
    Guid Id,
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
    List<UpdateProductVariationDto>? Variations,
    List<Guid>? SuggestedSideItemIds,
    List<ProductIngredientDto>? DetailedIngredients,
    MenuDefinitionDto? MenuDefinition,
    ProductDescriptionsDto? Content
) : ICommand<ApiResponse<ProductDto>>;

public class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand, ApiResponse<ProductDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateProductCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GetProductByIdQueryHandler> _getProductlogger;


    public UpdateProductCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<UpdateProductCommandHandler> logger,
        ILogger<GetProductByIdQueryHandler> getProductlogger,
        IConfiguration configuration
        )
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
        _getProductlogger = getProductlogger;
        _configuration = configuration;
    }

    public async Task<ApiResponse<ProductDto>> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .Include(p => p.ProductCategories)
            .Include(p => p.Descriptions)
            .Include(p => p.Variations)
                .ThenInclude(v => v.Descriptions)
            .Include(p => p.SuggestedSideItems)
            .Include(p => p.DetailedIngredients)
                .ThenInclude(di => di.Descriptions)
            .Include(p => p.MenuDefinition)
            .FirstOrDefaultAsync(p => p.Id == command.Id && !p.IsDeleted, cancellationToken);

        if (product == null)
        {
            return ApiResponse<ProductDto>.Failure("Product not found");
        }

        // Validate categories
        var categories = await _context.Categories
            .Where(c => command.CategoryIds.Contains(c.Id))
            .ToListAsync(cancellationToken);

        if (categories.Count != command.CategoryIds.Count)
        {
            return ApiResponse<ProductDto>.Failure("One or more categories not found");
        }

        // Update product properties
        product.Name = command.Name;
        product.Description = command.Description;
        product.BasePrice = command.BasePrice;
        product.IsActive = command.IsActive;
        product.IsAvailable = command.IsAvailable;
        product.IsSpecial = command.IsSpecial;
        product.PreparationTimeMinutes = command.PreparationTimeMinutes;
        product.Type = command.Type;
        product.KitchenType = command.KitchenType;
        product.Ingredients = command.Ingredients;
        product.Allergens = command.Allergens;
        product.DisplayOrder = command.DisplayOrder;
        product.UpdatedAt = DateTime.UtcNow;
        product.UpdatedBy = _currentUserService.GetAuditIdentifier();

        // Update categories
        _context.ProductCategories.RemoveRange(product.ProductCategories);

        var displayOrder = 0;
        foreach (var categoryId in command.CategoryIds)
        {
            var productCategory = new ProductCategory
            {
                ProductId = product.Id,
                CategoryId = categoryId,
                IsPrimary = categoryId == command.PrimaryCategoryId,
                DisplayOrder = displayOrder++,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier()
            };
            _context.ProductCategories.Add(productCategory);
        }


        // Content may be omitted from the request body (e.g. an edit that does not
        // touch translations); treat that as "no translation changes" rather than NRE-ing.
        var contentMap = command.Content ?? new ProductDescriptionsDto();

        var languageCodes = contentMap.Select(x => x.Key).ToList();
        var duplicateLanguageCodes = languageCodes.GroupBy(x => x)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateLanguageCodes.Any())
        {
            return ApiResponse<ProductDto>.Failure($"Duplicate language codes found: {string.Join(", ", duplicateLanguageCodes)}");
        }


        if (contentMap.Any())
        {
            _context.ProductDescriptions.RemoveRange(product.Descriptions);
        }

        foreach (var key in contentMap.Keys)
        {

            var content = contentMap[key];
            var productDescription = new ProductDescription()
            {
                UpdatedBy = _currentUserService.GetAuditIdentifier(),
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = _currentUserService.GetAuditIdentifier(),
                CreatedAt = DateTime.UtcNow,
                Description = content.Description,
                Lang = key,
                Name = content.Name,
                Product = product,
                ProductId = product.Id
            };
            await _context.ProductDescriptions.AddAsync(productDescription);
        }

        // Update variations
        if (command.Variations != null)
        {
            var incomingVariationIds = command.Variations
                .Where(v => v.Id.HasValue)
                .Select(v => v.Id!.Value)
                .ToList();

            // Remove variations not in the incoming list
            var variationsToRemove = product.Variations
                .Where(v => !incomingVariationIds.Contains(v.Id))
                .ToList();
            _context.ProductVariations.RemoveRange(variationsToRemove);

            foreach (var variationDto in command.Variations)
            {
                ProductVariation? variation;

                if (variationDto.Id.HasValue)
                {
                    // Update existing variation
                    variation = product.Variations.FirstOrDefault(v => v.Id == variationDto.Id.Value);
                    if (variation == null)
                    {
                        // Variation ID was provided but not found, skip or log error
                        _logger.LogWarning("Variation with ID {VariationId} not found for product {ProductId}",
                            variationDto.Id.Value, product.Id);
                        continue;
                    }

                    // Update properties
                    variation.Name = variationDto.Name;
                    variation.Description = variationDto.Description;
                    variation.PriceModifier = variationDto.PriceModifier;
                    variation.IsActive = variationDto.IsActive;
                    variation.DisplayOrder = variationDto.DisplayOrder;
                    variation.UpdatedAt = DateTime.UtcNow;
                    variation.UpdatedBy = _currentUserService.GetAuditIdentifier();

                    // Remove and recreate descriptions for existing variations
                    var existingDescriptions = await _context.ProductVariationDescriptions
                        .Where(d => d.ProductVariationId == variation.Id)
                        .ToListAsync(cancellationToken);
                    _context.ProductVariationDescriptions.RemoveRange(existingDescriptions);
                }
                else
                {
                    // Create new variation
                    variation = new ProductVariation
                    {
                        ProductId = product.Id,
                        Name = variationDto.Name,
                        Description = variationDto.Description,
                        PriceModifier = variationDto.PriceModifier,
                        IsActive = variationDto.IsActive,
                        DisplayOrder = variationDto.DisplayOrder,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };
                    await _context.ProductVariations.AddAsync(variation, cancellationToken);
                }

                // Add variation descriptions
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
                        await _context.ProductVariationDescriptions.AddAsync(description, cancellationToken);
                    }
                }
            }
        }

        // Update suggested side items
        if (command.SuggestedSideItemIds != null)
        {
            _context.ProductSideItems.RemoveRange(product.SuggestedSideItems);

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
                await _context.ProductSideItems.AddAsync(productSideItem, cancellationToken);
            }
        }

        // Update detailed ingredients
        if (command.DetailedIngredients != null)
        {
            // Remove existing ingredients and their descriptions
            var existingIngredients = product.DetailedIngredients.ToList();
            _context.ProductIngredients.RemoveRange(existingIngredients);

            // Add new ingredients
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

                await _context.ProductIngredients.AddAsync(ingredient, cancellationToken);

                // Add ingredient descriptions
                if (ingredientDto.Content != null)
                {
                    foreach (var (languageCode, content) in ingredientDto.Content)
                    {
                        var description = new ProductIngredientDescription
                        {
                            ProductIngredient = ingredient,
                            LanguageCode = languageCode,
                            Name = content.Name,
                            Description = content.Description,
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = _currentUserService.GetAuditIdentifier()
                        };
                        await _context.ProductIngredientDescriptions.AddAsync(description, cancellationToken);
                    }
                }
            }

            // Update Menu Definition
            if (command.Type == ProductType.Menu && command.MenuDefinition != null)
            {
                var menuDef = await _context.MenuDefinitions
                    .Include(m => m.Sections)
                        .ThenInclude(s => s.Items)
                    .FirstOrDefaultAsync(m => m.ProductId == product.Id, cancellationToken);

                if (menuDef == null)
                {
                    // Create new if not exists
                    menuDef = new MenuDefinition
                    {
                        ProductId = product.Id,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };
                    _context.MenuDefinitions.Add(menuDef);
                }

                // Update properties
                menuDef.IsAlwaysAvailable = command.MenuDefinition.IsAlwaysAvailable;
                menuDef.StartTime = command.MenuDefinition.StartTime;
                menuDef.EndTime = command.MenuDefinition.EndTime;
                menuDef.AvailableMonday = command.MenuDefinition.AvailableMonday;
                menuDef.AvailableTuesday = command.MenuDefinition.AvailableTuesday;
                menuDef.AvailableWednesday = command.MenuDefinition.AvailableWednesday;
                menuDef.AvailableThursday = command.MenuDefinition.AvailableThursday;
                menuDef.AvailableFriday = command.MenuDefinition.AvailableFriday;
                menuDef.AvailableSaturday = command.MenuDefinition.AvailableSaturday;
                menuDef.AvailableSunday = command.MenuDefinition.AvailableSunday;
                menuDef.UpdatedAt = DateTime.UtcNow;
                menuDef.UpdatedBy = _currentUserService.GetAuditIdentifier();

                // Update sections
                if (command.MenuDefinition.Sections != null)
                {
                    // Remove existing sections (simplest approach for now, can be optimized)
                    _context.MenuSections.RemoveRange(menuDef.Sections);

                    foreach (var sectionDto in command.MenuDefinition.Sections)
                    {
                        var section = new MenuSection
                        {
                            MenuDefinition = menuDef,
                            Name = sectionDto.Name,
                            Description = sectionDto.Description,
                            DisplayOrder = sectionDto.DisplayOrder,
                            IsRequired = sectionDto.IsRequired,
                            MinSelection = sectionDto.MinSelection,
                            MaxSelection = sectionDto.MaxSelection,
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = _currentUserService.GetAuditIdentifier()
                        };

                        _context.MenuSections.Add(section);

                        if (sectionDto.Items != null)
                        {
                            foreach (var itemDto in sectionDto.Items)
                            {
                                var item = new MenuSectionItem
                                {
                                    MenuSection = section,
                                    ProductId = itemDto.ProductId,
                                    AdditionalPrice = itemDto.AdditionalPrice,
                                    DisplayOrder = itemDto.DisplayOrder,
                                    IsDefault = itemDto.IsDefault,
                                    CreatedAt = DateTime.UtcNow,
                                    CreatedBy = _currentUserService.GetAuditIdentifier()
                                };
                                _context.MenuSectionItems.Add(item);
                            }
                        }
                    }
                }
            }
            else if (product.MenuDefinition != null && command.Type != ProductType.Menu)
            {
                // If type changed from Menu to something else, remove definition
                _context.MenuDefinitions.Remove(product.MenuDefinition);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        var handler = new GetProductByIdQueryHandler(_context, _getProductlogger, _configuration);
        var result = await handler.Handle(new GetProductByIdQuery(product.Id), cancellationToken);

        _logger.LogInformation("Product {ProductId} updated successfully by user {UserId}",
            product.Id, _currentUserService.UserId);

        return result;
    }
}


public record UpdateProductVariationDto(
    Guid? Id,
    string Name,
    string? Description,
    decimal PriceModifier,
    bool IsActive,
    int DisplayOrder,
    Dictionary<string, ProductVariationContentDto>? Content
);
