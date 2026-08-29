using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Features.Products.Queries.GetProductByIdQuery;
using RestaurantSystem.Api.Features.Products.Services;
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
    ProductDescriptionsDto? Content,
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
            // Multiple collection includes below — split to avoid a cartesian
            // explosion (matches GetProductByIdQueryHandler).
            .AsSplitQuery()
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
        product.AvailableOrderTypes = command.AvailableOrderTypes;
        product.IsSpecial = command.IsSpecial;
        product.HideBaseProduct = command.HideBaseProduct;
        product.IsComponent = command.IsComponent;
        product.SauceMin = command.SauceMin;
        product.SauceMax = command.SauceMax;
        product.SauceIncludedFree = command.SauceIncludedFree;
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

        // The duplicate-language-code check that used to stand here was DEAD and has been dropped
        // (#193), matching UpdateMenuBundleCommandHandler, which dropped its identical copy in #192.
        //
        // Dead by the TYPE's invariant, not merely by how requests happen to arrive — but the
        // invariant needs stating precisely, because the loose version of it is false.
        // Dictionary<string, …> CAN hold two ordinally-equal keys if it is constructed with a
        // comparer finer than ordinal (reference equality, say), and the old check grouped with
        // `GroupBy(x => x)`, i.e. the ordinal default — so such a dictionary would have made it
        // fire. What closes that door here is that ProductDescriptionsDto declares NO constructor:
        // C# does not inherit constructors, so the `Dictionary(IEqualityComparer<string>)` overload
        // is not callable on it and every instance carries the ordinal comparer. Verified by
        // reflection — exactly one public constructor, zero parameters.
        //
        // With the comparer pinned, grouping the keys and keeping groups of size > 1 yields an empty
        // list for EVERY possible value of `contentMap`, including one built in C# rather than
        // deserialized. The reachability argument therefore does not depend on the transport:
        // `[FromBody]` is the only production route today, but a future internal caller constructing
        // the command directly could not revive this branch either.
        //
        // On the transport specifically, and UNDER THE DEFAULT `AllowDuplicateProperties` (true on
        // .NET 10), System.Text.Json collapses duplicate JSON keys through the indexer, last-wins,
        // rather than throwing — a duplicate is silently merged, not rejected upstream. That is the
        // current default, not a property of the serializer: setting it false makes the same body a
        // JsonException, which only makes this branch more unreachable, but would turn the
        // last-wins test red. Measured both ways.
        //
        // Measured through this endpoint rather than reasoned: a raw body with two "fr" entries
        // answers 200 and writes ONE French description carrying the second entry's values. Forcing
        // the old branch to fire made it report `Duplicate language codes found: ` — with an empty
        // list, because the collection it interpolates was empty even on that body.
        //
        // Pinned by ProductUpdateContentTests, which also covers the two guards that are NOT dead
        // and share this block: the null coalesce above, and the `Any()` below. Both exist so an
        // edit that does not touch translations cannot wipe them all (#190).
        //
        // NOTE: TWO more copies of this same dead check still stand, both on CREATE paths —
        // CreateProductCommandHandler and CreateMenuBundleCommandHandler. `grep -rn "Duplicate
        // language codes"` finds both; do not treat this note as naming a single remaining site.
        //
        // Left alone because they are create paths with no content coverage of their own, NOT
        // because their `Content` is declared non-nullable. That distinction would be worthless:
        // UpdateMenuBundleCommand also declares `ProductDescriptionsDto Content` non-nullable and
        // still coalesces it, precisely because System.Text.Json binds an omitted JSON property to
        // null on a positional record parameter whatever the annotation says (#190). Both create
        // handlers instead dereference `command.Content` unguarded and neither validator requires
        // it — so an omitted `content` on a POST looks like a separate, pre-existing defect rather
        // than a safe contract. Tracked with the other content-validation gaps in #306.
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
            await _context.ProductDescriptions.AddAsync(productDescription, cancellationToken);
        }

        // Update variations
        if (command.Variations != null)
        {
            // S4 provenance, resolved once for the payload — see GlobalVariationProvenance for why a
            // link the row already carries is never re-checked.
            var variationProvenance = await GlobalVariationProvenance.ResolveAsync(
                _context,
                command.Variations.Select(v => v.GlobalVariationId),
                _logger,
                cancellationToken);

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
                    variation.GlobalVariationId = variationProvenance.LinkFor(
                        variationDto.GlobalVariationId, variationDto.Name, variation.GlobalVariationId);
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
                        GlobalVariationId = variationProvenance.LinkFor(variationDto.GlobalVariationId, variationDto.Name),
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

        // Update detailed ingredients — BY ID, never remove-and-recreate.
        //
        // Orders and baskets key their customisation off the ingredient id
        // (`IngredientQuantitiesJson` is a `{ ingredientId: quantity }` map), so re-creating these
        // rows on every save silently blanked the ingredient detail of every past order. The diff
        // lives in ProductIngredientSynchronizer with the full argument; §4 also forbids growing
        // this file, which is already baselined over its 200-line limit.
        if (command.DetailedIngredients != null)
        {
            await ProductIngredientSynchronizer.SyncAsync(
                _context,
                product,
                command.DetailedIngredients,
                _currentUserService.GetAuditIdentifier(),
                _logger,
                cancellationToken);
        }

        // Update Menu Definition.
        //
        // At STATEMENT level, not inside the detailed-ingredients branch above (#296). It used to
        // sit one level deeper — brace-verified, since the indentation agreed and made the nesting
        // read as intentional — so `PUT /api/Products/{id}` carrying a menuDefinition but no
        // `detailedIngredients` key returned 200 having silently discarded the entire menu half of
        // the request: no schedule fields, no sections, and no orphan cleanup on a type change away
        // from Menu.
        //
        // No browser payload could reach it, for two independent reasons — worth separating,
        // because only the first is about this branch. (1) `submitEditProductForm` dispatches on
        // `data.menuDefinition`: truthy goes to `updateMenuBundle` (PUT /api/Menus), falsy sends
        // `toMenuDefinitionPayload(undefined)` — which returns undefined, so the key is absent. A
        // menuDefinition therefore never arrives HERE from the admin editor at all. (2) The same
        // form always sends `detailedIngredients` (empty at worst), so the orphan `else if`, which
        // needs no menuDefinition in the command, ran correctly for every payload the editor
        // produces. The defect was reachable only by an API client that omits detailedIngredients —
        // a supported shape, since the field is nullable on the command and means "no ingredient
        // instruction", not "no menu instruction".
        if (command.Type == ProductType.Menu && command.MenuDefinition != null)
        {
            // Resolved FIRST, before any query or mutation — the same dead guard, and the same
            // wipe, as the bundle handler carried (#191). Fixing only
            // UpdateMenuBundleCommandHandler would have left this path able to erase a bundle's
            // sections: `PUT /api/Products` on a Menu-type product reaches here, and
            // MenuDefinitionDto is the same shared DTO. UpdateProductCommandValidator requires the
            // key whenever a menu definition is sent for a Menu-type product, so null cannot
            // arrive; the throw keeps that true loudly rather than defaulting back into the wipe.
            //
            // Hoisted above the assignments rather than left beside its use because this handler
            // runs in NO transaction: throwing after the schedule fields were written would still
            // be safe today (the single SaveChangesAsync is further down), but failing before
            // touching the entity at all does not depend on that staying true.
            var sections = command.MenuDefinition.Sections
                ?? throw new BadRequestException(MenuDefinitionDto.SectionsRequiredMessage);

            // A SECOND query, deliberately: the product query above includes MenuDefinition but not
            // its Sections, and ReplaceSections reads an un-included collection as empty rather
            // than null — it would append instead of replacing, with no exception anywhere. The
            // bundle handler gets the same guarantee from its own ThenInclude. EF returns the
            // already-tracked instance here, so this populates the navigation rather than
            // producing a second entity.
            var existing = await _context.MenuDefinitions
                .Include(m => m.Sections)
                    .ThenInclude(s => s.Items)
                .FirstOrDefaultAsync(m => m.ProductId == product.Id, cancellationToken);

            var auditIdentifier = _currentUserService.GetAuditIdentifier();
            var menuDef = MenuDefinitionWriter.Upsert(
                _context, existing, product.Id, command.MenuDefinition, auditIdentifier);

            MenuSectionWriter.ReplaceSections(_context, menuDef, sections, auditIdentifier);
        }
        else if (product.MenuDefinition != null && command.Type != ProductType.Menu)
        {
            // If type changed from Menu to something else, remove definition
            _context.MenuDefinitions.Remove(product.MenuDefinition);
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
    Dictionary<string, ProductVariationContentDto>? Content,
    // S4 provenance. Last and defaulted, so every existing caller keeps compiling.
    Guid? GlobalVariationId = null
);
