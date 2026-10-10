using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Menus;
using RestaurantSystem.Api.Features.Products.Dtos;
using System.Text.Json.Serialization;
using RestaurantSystem.Api.Features.Products.Queries.GetProductByIdQuery;
using RestaurantSystem.Api.Features.Products.Services;
using RestaurantSystem.Api.Features.TranslationWorkbench.Dtos;
using RestaurantSystem.Api.Features.TranslationWorkbench.Services;
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
    bool IsComponent = false,
    List<ProductCustomizationGroupDto>? CustomizationGroups = null,
    TranslationOwnerMetadataDto? TranslationMetadata = null
) : ICommand<ApiResponse<ProductDto>>
{
    private CustomerStepManifestDto? _customerStepManifest;

    [JsonIgnore]
    public bool CustomerStepManifestSpecified { get; private set; }

    public CustomerStepManifestDto? CustomerStepManifest
    {
        get => _customerStepManifest;
        init
        {
            _customerStepManifest = value;
            CustomerStepManifestSpecified = true;
        }
    }
}

public class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand, ApiResponse<ProductDto>>
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateProductCommandHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GetProductByIdQueryHandler> _getProductlogger;
    private readonly IProductCustomizationGroupSynchronizer _customizationGroupSynchronizer;
    private readonly ITranslationProvenanceWriter _translationProvenanceWriter;


    public UpdateProductCommandHandler(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        ILogger<UpdateProductCommandHandler> logger,
        ILogger<GetProductByIdQueryHandler> getProductlogger,
        IConfiguration configuration,
        IProductCustomizationGroupSynchronizer customizationGroupSynchronizer,
        ITranslationProvenanceWriter? translationProvenanceWriter = null
        )
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
        _getProductlogger = getProductlogger;
        _configuration = configuration;
        _customizationGroupSynchronizer = customizationGroupSynchronizer;
        _translationProvenanceWriter = translationProvenanceWriter ??
            new TranslationProvenanceWriter(context, currentUserService);
    }

    public async Task<ApiResponse<ProductDto>> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        try
        {
            var result = await HandleWithinTransactionAsync(command, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(rollbackEx, "Transaction rollback failed during product update");
            }

            MenuOfferLinkConflict.ThrowIfExpected(exception);
            if (CustomerStepManifestStore.IsRevisionWriteConflict(exception))
                throw await CustomerStepManifestStore.RevisionWriteConflictAsync(
                    _context, command.Id, exception, cancellationToken);
            throw;
        }
    }

    private async Task<ApiResponse<ProductDto>> HandleWithinTransactionAsync(
        UpdateProductCommand command,
        CancellationToken cancellationToken)
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
            .Include(p => p.CustomizationGroups)
                .ThenInclude(group => group.Descriptions)
            .Include(p => p.CustomizationGroups)
                .ThenInclude(group => group.IngredientOptions)
            .Include(p => p.CustomizationGroups)
                .ThenInclude(group => group.ProductOptions)
            .Include(p => p.MenuDefinition)
                .ThenInclude(definition => definition!.Sections)
                    .ThenInclude(section => section.Items)
            .FirstOrDefaultAsync(p => p.Id == command.Id && !p.IsDeleted, cancellationToken);

        if (product == null)
        {
            return ApiResponse<ProductDto>.Failure("Product not found");
        }

        TranslationContentVersion.EnsureCurrent(product,
            command.TranslationMetadata?.ExpectedContentVersion,
            command.TranslationMetadata?.AcceptedSuggestionIds?.Count ?? 0);

        await MenuOfferLinkRules.EnsureCanChangeTypeAsync(
            _context, product, command.Type, cancellationToken);
        await MenuOfferLinkRules.EnsureCanDeactivateAsync(
            _context, product.Id, command.IsActive, cancellationToken);
        await MenuOfferLinkRules.EnsureCanBecomeComponentAsync(
            _context, product.Id, command.IsComponent, cancellationToken);

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
        AddProductCategories(product, command);

        await UpdateProductContentAsync(product, command.Content, cancellationToken);
        await _translationProvenanceWriter.RecordAsync("product", product.Id,
            command.TranslationMetadata,
            TranslationTextMap.Create(command.Name, command.Description,
                (command.Content ?? []).Select(pair =>
                    (pair.Key, (string?)pair.Value.Name, (string?)pair.Value.Description))),
            cancellationToken);

        // Update variations
        await UpdateVariationsAsync(product, command.Variations, cancellationToken);

        // Update suggested side items
        await UpdateSuggestedSideItemsAsync(product, command.SuggestedSideItemIds, cancellationToken);

        // Update detailed ingredients — BY ID, never remove-and-recreate.
        //
        // Orders and baskets key their customisation off the ingredient id
        // (`IngredientQuantitiesJson` is a `{ ingredientId: quantity }` map), so re-creating these
        // rows on every save silently blanked the ingredient detail of every past order. The diff
        // lives in ProductIngredientSynchronizer with the full argument; §4 also forbids growing
        // this file, which is already baselined over its 200-line limit.
        await UpdateDetailedIngredientsAsync(product, command.DetailedIngredients, cancellationToken);
        await UpdateCustomizationGroupsAsync(product, command.CustomizationGroups, cancellationToken);

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
        await UpdateMenuDefinitionAsync(product, command, cancellationToken);

        await CustomerStepManifestStore.ApplyAsync(_context, product,
            command.CustomerStepManifestSpecified, command.CustomerStepManifest, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        var handler = new GetProductByIdQueryHandler(_context, _getProductlogger, _configuration);
        var result = await handler.Handle(new GetProductByIdQuery(product.Id), cancellationToken);

        _logger.LogInformation("Product {ProductId} updated successfully by user {UserId}",
            product.Id, _currentUserService.UserId);

        return result;
    }

    private void AddProductCategories(Product product, UpdateProductCommand command)
    {
        var auditIdentifier = _currentUserService.GetAuditIdentifier();
        var categories = command.CategoryIds.Select((categoryId, displayOrder) => new ProductCategory
        {
            ProductId = product.Id,
            CategoryId = categoryId,
            IsPrimary = categoryId == command.PrimaryCategoryId,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = auditIdentifier
        });

        _context.ProductCategories.AddRange(categories);
    }

    private async Task UpdateProductContentAsync(
        Product product,
        ProductDescriptionsDto? content,
        CancellationToken cancellationToken)
    {
        // An omitted content map means "no translation changes". A non-empty map replaces the
        // complete set, matching the previous inline update behavior.
        var contentMap = content ?? new ProductDescriptionsDto();
        if (!contentMap.Any())
        {
            return;
        }

        _context.ProductDescriptions.RemoveRange(product.Descriptions);
        var auditIdentifier = _currentUserService.GetAuditIdentifier();
        foreach (var (languageCode, value) in contentMap)
        {
            var productDescription = new ProductDescription
            {
                UpdatedBy = auditIdentifier,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier,
                CreatedAt = DateTime.UtcNow,
                Description = value.Description,
                Lang = languageCode,
                Name = value.Name,
                Product = product,
                ProductId = product.Id
            };
            await _context.ProductDescriptions.AddAsync(productDescription, cancellationToken);
        }
    }

    private async Task UpdateVariationsAsync(
        Product product,
        List<UpdateProductVariationDto>? variations,
        CancellationToken cancellationToken)
    {
        if (variations is null)
        {
            return;
        }

        await EnsureVariationsCanBeDeactivatedAsync(product, variations, cancellationToken);
        var variationProvenance = await GlobalVariationProvenance.ResolveAsync(
            _context,
            variations.Select(variation => variation.GlobalVariationId),
            _logger,
            cancellationToken);
        var variationPromotion = await CustomVariationPromotion.PrepareAsync(
            _context,
            variations.Select(variation => (variation.GlobalVariationId, variation.Name, variation.Content)),
            _currentUserService.GetAuditIdentifier(),
            cancellationToken);

        var incomingVariationIds = variations
            .Where(variation => variation.Id.HasValue)
            .Select(variation => variation.Id!.Value)
            .ToList();
        var variationsToRemove = product.Variations
            .Where(variation => !incomingVariationIds.Contains(variation.Id))
            .ToList();
        _context.ProductVariations.RemoveRange(variationsToRemove);

        foreach (var variationDto in variations)
        {
            await UpdateVariationAsync(product, variationDto, variationProvenance, variationPromotion,
                cancellationToken);
        }
    }

    private async Task EnsureVariationsCanBeDeactivatedAsync(
        Product product,
        IReadOnlyCollection<UpdateProductVariationDto> incomingVariations,
        CancellationToken cancellationToken)
    {
        // Keep the first matching incoming row semantics of the original lookup while collecting
        // all inactive ids for the two set-based relationship checks.
        var variationIdsToCheck = product.Variations
            .Select(variation => new
            {
                variation.Id,
                Incoming = incomingVariations.FirstOrDefault(candidate => candidate.Id == variation.Id)
            })
            .Where(entry => entry.Incoming?.IsActive != true)
            .Select(entry => entry.Id)
            .ToList();

        await MenuOfferLinkRules.EnsureCanDeactivateVariationsAsync(
            _context, variationIdsToCheck, cancellationToken);
    }

    private async Task UpdateVariationAsync(
        Product product,
        UpdateProductVariationDto variationDto,
        GlobalVariationProvenance variationProvenance,
        CustomVariationPromotion variationPromotion,
        CancellationToken cancellationToken)
    {
        ProductVariation? variation;
        var auditIdentifier = _currentUserService.GetAuditIdentifier();

        if (variationDto.Id.HasValue)
        {
            variation = product.Variations.FirstOrDefault(candidate => candidate.Id == variationDto.Id.Value);
            if (variation is null)
            {
                _logger.LogWarning("Variation with ID {VariationId} not found for product {ProductId}",
                    variationDto.Id.Value, product.Id);
                return;
            }

            variation.Name = variationDto.Name;
            variation.Description = variationDto.Description;
            variation.PriceModifier = variationDto.PriceModifier;
            variation.IsActive = variationDto.IsActive;
            variation.DisplayOrder = variationDto.DisplayOrder;
            variation.GlobalVariationId = variationProvenance.LinkFor(
                variationDto.GlobalVariationId, variationDto.Name, variation.GlobalVariationId)
                ?? variationPromotion.IdFor(variationDto.Name);
            variation.UpdatedAt = DateTime.UtcNow;
            variation.UpdatedBy = auditIdentifier;

            var existingDescriptions = await _context.ProductVariationDescriptions
                .Where(description => description.ProductVariationId == variation.Id)
                .ToListAsync(cancellationToken);
            _context.ProductVariationDescriptions.RemoveRange(existingDescriptions);
        }
        else
        {
            variation = new ProductVariation
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Name = variationDto.Name,
                Description = variationDto.Description,
                PriceModifier = variationDto.PriceModifier,
                IsActive = variationDto.IsActive,
                DisplayOrder = variationDto.DisplayOrder,
                GlobalVariationId = variationProvenance.LinkFor(variationDto.GlobalVariationId, variationDto.Name)
                    ?? variationPromotion.IdFor(variationDto.Name),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier
            };
            await _context.ProductVariations.AddAsync(variation, cancellationToken);
        }

        if (variationDto.Content is null)
        {
            await RecordVariationMetadataAsync(variationDto, variation, cancellationToken);
            return;
        }

        foreach (var (languageCode, content) in variationDto.Content)
        {
            var description = new ProductVariationDescription
            {
                ProductVariation = variation,
                LanguageCode = languageCode,
                Name = content.Name,
                Description = content.Description,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier
            };
            await _context.ProductVariationDescriptions.AddAsync(description, cancellationToken);
        }
        await RecordVariationMetadataAsync(variationDto, variation, cancellationToken);
    }

    private Task RecordVariationMetadataAsync(
        UpdateProductVariationDto dto,
        ProductVariation variation,
        CancellationToken cancellationToken) => _translationProvenanceWriter.RecordAsync(
            "productVariation", variation.Id, dto.TranslationMetadata,
            TranslationTextMap.Create(dto.Name, dto.Description,
                (dto.Content ?? []).Select(pair =>
                    (pair.Key, (string?)pair.Value.Name, pair.Value.Description))),
            cancellationToken);

    private async Task UpdateSuggestedSideItemsAsync(
        Product product,
        List<Guid>? suggestedSideItemIds,
        CancellationToken cancellationToken)
    {
        if (suggestedSideItemIds is null)
        {
            return;
        }

        var existingByProductId = product.SuggestedSideItems
            .OrderBy(side => side.DisplayOrder)
            .ThenBy(side => side.Id)
            .GroupBy(side => side.SideItemProductId)
            .ToDictionary(group => group.Key, group => new Queue<ProductSideItem>(group));
        var retained = new HashSet<ProductSideItem>();
        var additions = new List<ProductSideItem>();
        var auditIdentifier = _currentUserService.GetAuditIdentifier();

        for (var displayOrder = 0; displayOrder < suggestedSideItemIds.Count; displayOrder++)
        {
            var sideItemId = suggestedSideItemIds[displayOrder];
            if (existingByProductId.TryGetValue(sideItemId, out var matches) && matches.TryDequeue(out var existing))
            {
                // The association row is the stable manifest scope ID. Keep its identity and
                // requiredness when a full product PUT merely reorders the same side product.
                existing.DisplayOrder = displayOrder;
                retained.Add(existing);
                continue;
            }

            var added = new ProductSideItem
            {
                MainProductId = product.Id,
                SideItemProductId = sideItemId,
                IsRequired = false,
                DisplayOrder = displayOrder,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = auditIdentifier
            };
            additions.Add(added);
            product.SuggestedSideItems.Add(added);
        }

        var removed = product.SuggestedSideItems.Where(side => !retained.Contains(side)
            && !additions.Contains(side)).ToList();
        _context.ProductSideItems.RemoveRange(removed);
        foreach (var side in removed)
            product.SuggestedSideItems.Remove(side);
        await _context.ProductSideItems.AddRangeAsync(additions, cancellationToken);
    }

    private async Task UpdateDetailedIngredientsAsync(
        Product product,
        List<ProductIngredientDto>? detailedIngredients,
        CancellationToken cancellationToken)
    {
        if (detailedIngredients is null)
        {
            return;
        }

        await ProductIngredientSynchronizer.SyncAsync(
            _context,
            product,
            detailedIngredients,
            _currentUserService.GetAuditIdentifier(),
            _logger,
            cancellationToken,
            (dto, ingredient) => _translationProvenanceWriter.RecordAsync(
                "productIngredient", ingredient.Id, dto.TranslationMetadata,
                TranslationTextMap.Create(dto.Name, null,
                    (dto.Content ?? []).Select(pair =>
                        (pair.Key, (string?)pair.Value.Name, pair.Value.Description))),
                cancellationToken));
    }

    private async Task UpdateCustomizationGroupsAsync(
        Product product,
        List<ProductCustomizationGroupDto>? customizationGroups,
        CancellationToken cancellationToken)
    {
        if (customizationGroups is null)
        {
            return;
        }

        await _customizationGroupSynchronizer.SyncAsync(
            product,
            customizationGroups,
            _currentUserService.GetAuditIdentifier(),
            cancellationToken);
    }

    private async Task UpdateMenuDefinitionAsync(
        Product product,
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        if (product.MenuDefinition?.VersionedSectionEditingStarted == true
            && command.Type != ProductType.Menu)
        {
            throw new ConflictException(
                "This menu has entered versioned section editing and cannot be converted through the legacy product PUT. Use the menu authoring API to preserve its section IDs.");
        }

        if (command.Type == ProductType.Menu && command.MenuDefinition is not null)
        {
            await WriteMenuDefinitionAsync(product, command.MenuDefinition, command.IsComponent, cancellationToken);
            return;
        }

        if (product.MenuDefinition is not null && command.Type != ProductType.Menu)
        {
            _context.MenuDefinitions.Remove(product.MenuDefinition);
        }
    }

    private async Task WriteMenuDefinitionAsync(
        Product product,
        MenuDefinitionDto menuDefinition,
        bool isComponent,
        CancellationToken cancellationToken)
    {
        var sections = menuDefinition.Sections
            ?? throw new BadRequestException(MenuDefinitionDto.SectionsRequiredMessage);

        if (menuDefinition.OfferParentSpecified)
        {
            await MenuOfferLinkRules.EnsureValidAsync(
                _context,
                product.Id,
                menuDefinition.ParentOfferProductId,
                menuDefinition.ParentOfferVariationId,
                cancellationToken,
                isComponent);
        }

        // Menu sections are loaded separately because the product query intentionally includes only
        // the definition. The tracked instance is reused by EF for the replacement operation.
        var existing = await _context.MenuDefinitions
            .AsSplitQuery()
            .Include(menu => menu.Sections)
                .ThenInclude(section => section.Items)
            .Include(menu => menu.Sections)
                .ThenInclude(section => section.Translations)
            .FirstOrDefaultAsync(menu => menu.ProductId == product.Id, cancellationToken);

        await MenuSectionVariationValidator.ValidateWithExistingAsync(
            _context, sections, existing?.Sections ?? [], cancellationToken);

        // Compare the incoming full-replacement snapshot to the persisted sections before Upsert
        // changes the schedule or offer-parent fields. Once versioned editing starts, unchanged
        // snapshots may accompany other product edits; changed snapshots must use the PATCH API.
        var replaceSections = existing is null
            || MenuSectionReplacementGuard.ShouldReplaceSections(existing, sections);

        var auditIdentifier = _currentUserService.GetAuditIdentifier();
        var menuDef = MenuDefinitionWriter.Upsert(
            _context, existing, product.Id, menuDefinition, auditIdentifier);
        IReadOnlyList<(MenuSectionDto Input, MenuSection Entity)> written;
        if (replaceSections)
        {
            written = MenuSectionWriter.ReplaceSections(_context, menuDef, sections, auditIdentifier);
        }
        else
        {
            var byId = menuDef.Sections.ToDictionary(section => section.Id);
            written = sections.Where(section => section.Id.HasValue && byId.ContainsKey(section.Id.Value))
                .Select(section => (section, byId[section.Id.GetValueOrDefault()])).ToArray();
        }

        foreach (var (input, section) in written)
        {
            await _translationProvenanceWriter.RecordAsync("menuSection", section.Id,
                input.TranslationMetadata, TranslationTextMap.FromSection(input), cancellationToken);
        }
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
    Guid? GlobalVariationId = null,
    TranslationOwnerMetadataDto? TranslationMetadata = null
);
