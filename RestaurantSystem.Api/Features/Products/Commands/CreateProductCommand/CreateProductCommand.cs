using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Services;
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
    int? AvailableOrderTypes = null,
    // Hide the "no variation" base row so the guest must pick one. Optional and last so existing
    // callers keep today's behaviour (Track F / F2).
    bool HideBaseProduct = false,
    // The sauce group rule (S5). Admin-editable per product with NO tenant default; the neutral
    // seeds below are what every product has today — nothing required, no cap, nothing free.
    // `SauceMax = null` is "no group cap", NOT 0. Semantics live on the Product entity.
    int SauceMin = 0,
    int? SauceMax = null,
    int SauceIncludedFree = 0,
    // A bundle COMPONENT: not listed in the catalogue and not orderable on its own (see
    // Product.IsComponent). Optional and last so every existing caller and test payload keeps
    // compiling and keeps meaning "an ordinary catalogue item".
    bool IsComponent = false
) : ICommand<ApiResponse<ProductDto>>;

public record CreateProductVariationDto(
    string Name,
    string? Description,
    decimal PriceModifier,
    bool IsActive,
    int DisplayOrder,
    Dictionary<string, ProductVariationContentDto>? Content,
    // S4 provenance. Last and defaulted, so every existing caller and every existing test payload
    // keeps compiling and keeps meaning "typed by hand".
    Guid? GlobalVariationId = null
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
                HideBaseProduct = command.HideBaseProduct,
                IsComponent = command.IsComponent,
                SauceMin = command.SauceMin,
                SauceMax = command.SauceMax,
                SauceIncludedFree = command.SauceIncludedFree,
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
                // One query for the whole payload, and none at all when nothing carries a link.
                var variationProvenance = await GlobalVariationProvenance.ResolveAsync(
                    _context,
                    command.Variations.Select(v => v.GlobalVariationId),
                    _logger,
                    cancellationToken);

                // …and one for the rows the payload does NOT link, so a size typed straight into
                // the editor lands in the tenant's own library too (plan D14).
                var variationPromotion = await CustomVariationPromotion.PrepareAsync(
                    _context,
                    command.Variations.Select(v => (v.GlobalVariationId, v.Name, v.Content)),
                    _currentUserService.GetAuditIdentifier(),
                    cancellationToken);

                foreach (var variationDto in command.Variations)
                {
                    var variation = new ProductVariation
                    {
                        Name = variationDto.Name,
                        Description = variationDto.Description,
                        PriceModifier = variationDto.PriceModifier,
                        IsActive = variationDto.IsActive,
                        DisplayOrder = variationDto.DisplayOrder,
                        GlobalVariationId = variationProvenance.LinkFor(variationDto.GlobalVariationId, variationDto.Name)
                            ?? variationPromotion.IdFor(variationDto.Name),
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };
                    _context.ProductVariations.Add(variation);
                    product.Variations.Add(variation);

                    if (variationDto.Content != null)
                    {
                        foreach (var (languageCode, content) in variationDto.Content)
                        {
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
                var provenance = await GlobalIngredientProvenance.ResolveAsync(
                    _context, command.DetailedIngredients, _logger, cancellationToken);

                // Hand-typed names earn a place in the tenant's own library (plan D14) — see
                // CustomIngredientPromotion. Same rule on the update path, in the synchroniser.
                var ingredientPromotion = await CustomIngredientPromotion.PrepareAsync(
                    _context, command.DetailedIngredients, _currentUserService.GetAuditIdentifier(), cancellationToken);

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
                        Kind = ingredientDto.Kind,
                        // §9: normalised at the write path, so a cleared input ("") is stored as
                        // "no group" and never as one anonymous group shared by every cleared row.
                        ExclusionGroup = IngredientExclusionGroupRule.Normalize(ingredientDto.ExclusionGroup),
                        // Provenance of a picked library row — or of the row a hand-typed name was
                        // just promoted into.
                        GlobalIngredientId = provenance.LinkFor(ingredientDto)
                            ?? ingredientPromotion.IdFor(ingredientDto.Name, ingredientDto.Kind),
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = _currentUserService.GetAuditIdentifier()
                    };

                    _context.ProductIngredients.Add(ingredient);
                    product.DetailedIngredients.Add(ingredient);

                    // Add ingredient descriptions (the validator has already refused a blank name)
                    if (ingredientDto.Content != null)
                    {
                        // The blank-entry skip that used to stand here is GONE (#323). It was kept in
                        // #316 because deleting it alone persisted junk — an `{"name":"","description":""}`
                        // row whose `en` language code the `en` locale then matches, shadowing the
                        // ingredient's real name with nothing. What has changed is that the validator now
                        // refuses a blank Name outright, so no such entry reaches this loop, and the
                        // three-policies-across-four-paths asymmetry that skip left behind is closed.
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
                            _context.ProductIngredientDescriptions.Add(description);
                            ingredient.Descriptions.Add(description);
                        }
                    }
                }

            }

            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            var createdProduct = await _context.Products
                .WithProductDtoNavigations()
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
