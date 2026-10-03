using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalog;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Services;

public sealed class ChannelCatalogueInventoryReader(ApplicationDbContext context,
    IOptions<DeliveryChannelSettings> options) : IChannelCatalogueInventoryReader
{
    private const int MaximumSelection = 200;

    public async Task<ChannelCatalogueInventorySnapshot> Read(string provider, string storeId, string currency,
        bool isSandbox, string language, CancellationToken cancellationToken)
    {
        ValidateBinding(provider, storeId, currency, isSandbox, language);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var actualCurrency = await context.RestaurantInfo.AsNoTracking().Select(row => row.Currency).SingleAsync(cancellationToken);
        if (actualCurrency != currency) throw new BadRequestException("Marketplace and tenant currency must match.");

        if (await context.Categories.CountAsync(cancellationToken) > ExternalOrderLimits.MaximumCatalogueCategories)
            throw new BadRequestException("This catalogue has too many categories for delivery-channel management.", "CategoryLimitExceeded");
        var categoryEntities = await context.Categories.AsNoTracking().Include(row => row.Translations)
            .OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id).ToArrayAsync(cancellationToken);
        var products = await context.Products.AsNoTracking().Where(row => !row.IsComponent)
            .Include(row => row.Descriptions).Include(row => row.Variations).ThenInclude(row => row.Descriptions)
            .Include(row => row.CustomizationGroups).ThenInclude(row => row.IngredientOptions)
            .Include(row => row.CustomizationGroups).ThenInclude(row => row.ProductOptions)
            .Include(row => row.DetailedIngredients)
            .Include(row => row.ProductCategories).ThenInclude(row => row.Category).ThenInclude(row => row.Translations)
            .AsSplitQuery().ToArrayAsync(cancellationToken);

        var items = products.SelectMany(product => ProductItems(product, language)).ToArray();
        var categorySummaries = categoryEntities.Select(category => Category(category, items, language)).ToArray();
        var revision = Revision(provider, storeId, currency, isSandbox, language, categoryEntities, categorySummaries, items);
        await transaction.CommitAsync(cancellationToken);
        return new(provider, storeId, currency, isSandbox, language, revision, categorySummaries, items);
    }

    public async Task<ChannelCatalogueSelectionSnapshot> ReadSelection(ChannelCatalogueSelectionSnapshotRequest request,
        string provider, string storeId, string currency, bool isSandbox, string language,
        CancellationToken cancellationToken)
    {
        ValidateSelectionRequest(request);
        if (string.IsNullOrEmpty(request.ExpectedSourceRevision) || request.ExpectedSourceRevision.Length != 64)
            throw new BadRequestException("Refresh the catalogue before saving its selection.");
        var inventory = await Read(provider, storeId, currency, isSandbox, language, cancellationToken);
        if (!string.Equals(inventory.Revision, request.ExpectedSourceRevision, StringComparison.Ordinal))
            throw new ConflictException("The tenant catalogue changed. Refresh categories and review the selection again.", "SourceRevisionChanged");
        var categories = inventory.Categories.ToDictionary(row => row.CategoryId);
        var selectedCategories = request.CategoryIds.Distinct().OrderBy(id => id).ToArray();
        if (selectedCategories.Length != request.CategoryIds.Count || selectedCategories.Any(id => !categories.ContainsKey(id)))
            throw new BadRequestException("Choose categories from the current tenant catalogue.");
        var inventoryByIdentity = inventory.Items.ToDictionary(row => (row.ProductId, row.VariationId));
        var overrides = ValidateOverrides(request.ItemOverrides, inventoryByIdentity);
        var selected = inventory.Items.Where(item => item.CategoryId is { } categoryId && selectedCategories.Contains(categoryId))
            .ToDictionary(item => (item.ProductId, item.VariationId));
        foreach (var row in overrides)
        {
            var identity = (row.ProductId, row.VariationId);
            if (row.Selected) selected[identity] = inventoryByIdentity[identity];
            else selected.Remove(identity);
        }
        if (selected.Count == 0)
            throw new BadRequestException("Select at least one marketplace item.", "SelectionRequired");
        if (selected.Count > MaximumSelection)
            throw new BadRequestException("Select no more than 200 marketplace items.", "SelectionLimitExceeded");

        var normalizedOverrides = overrides.Where(row =>
        {
            var source = inventoryByIdentity[(row.ProductId, row.VariationId)];
            var categoryDefault = source.CategoryId is { } id && selectedCategories.Contains(id);
            return row.Selected != categoryDefault;
        }).ToArray();
        var summaries = inventory.Categories.Select(category =>
        {
            var categoryItems = selected.Values.Where(item => item.CategoryId == category.CategoryId).ToArray();
            return new ChannelCatalogueSelectionCategoryDto(category.CategoryId, category.Name, category.DisplayOrder,
                category.TotalItemCount, category.SupportedItemCount, category.UnsupportedItemCount, categoryItems.Length,
                categoryItems.Count(item => !item.Supported), category.Active);
        }).ToArray();
        var overrideSnapshots = normalizedOverrides.Select(row =>
        {
            var item = inventoryByIdentity[(row.ProductId, row.VariationId)];
            return new ChannelCatalogueItemOverrideSnapshotDto(row.ProductId, row.VariationId, row.CategoryId,
                row.Selected, item.SelectionKey, item.SourceFingerprint, item.Supported);
        }).ToArray();
        return new(provider, storeId, currency, isSandbox, language, inventory.Revision, summaries, selectedCategories,
            overrideSnapshots, Ordered(selected.Values));
    }

    private static ChannelCatalogueItemOverrideRequest[] ValidateOverrides(
        IReadOnlyList<ChannelCatalogueItemOverrideRequest> rows,
        Dictionary<(Guid ProductId, Guid? VariationId), ChannelCatalogueInventoryItemDto> inventory)
    {
        if (rows.Count > ExternalOrderLimits.MaximumCatalogueItemOverrides)
            throw new BadRequestException("Review no more than 2,000 individual item overrides at a time.", "SelectionOverrideLimitExceeded");
        if (rows.Count > inventory.Count || rows.Any(row => row is null || row.ProductId == Guid.Empty || row.VariationId == Guid.Empty)
            || rows.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != rows.Count)
            throw new BadRequestException("Review each product or variation only once.");
        foreach (var row in rows)
        {
            if (!inventory.TryGetValue((row.ProductId, row.VariationId), out var source)
                || row.CategoryId != source.CategoryId || row.CategoryId is null)
                throw new BadRequestException("An item selection no longer matches its tenant category.");
        }
        return rows.OrderBy(row => row.CategoryId).ThenBy(row => row.ProductId).ThenBy(row => row.VariationId).ToArray();
    }

    private static void ValidateSelectionRequest(ChannelCatalogueSelectionSnapshotRequest request)
    {
        if (request is null || request.CategoryIds is null || request.ItemOverrides is null)
            throw new BadRequestException("Refresh the catalogue before saving its selection.");
        if (request.CategoryIds.Count > ExternalOrderLimits.MaximumCatalogueCategories)
            throw new BadRequestException("Review no more than 1,000 categories at a time.", "CategoryLimitExceeded");
        if (request.ItemOverrides.Count > ExternalOrderLimits.MaximumCatalogueItemOverrides)
            throw new BadRequestException("Review no more than 2,000 individual item overrides at a time.", "SelectionOverrideLimitExceeded");
    }

    private static ChannelCatalogueInventoryItemDto[] ProductItems(Product product, string language)
    {
        var primary = LiveProductCategories.Of(product).SingleOrDefault(row => row.IsPrimary);
        var variations = product.Variations.Where(row => row.IsActive && !row.IsDeleted).ToArray();
        var identities = new List<Guid?>();
        if (!BaseProductVisibility.IsBaseHidden(product)) identities.Add(null);
        identities.AddRange(variations.OrderBy(row => row.DisplayOrder).ThenBy(row => row.Id).Select(row => (Guid?)row.Id));
        return identities.Select(variationId => Item(product, variationId, primary, language)).ToArray();
    }

    private static ChannelCatalogueInventoryItemDto Item(Product product, Guid? variationId,
        ProductCategory? primary, string language)
    {
        var selection = new ChannelAvailabilitySelection(product.Id, variationId);
        var mapped = ChannelCatalogueReader.Map(selection, product, language);
        var variation = variationId is { } id ? product.Variations.Single(row => row.Id == id) : null;
        var category = primary?.Category;
        var reason = mapped.BlockReason;
        if (reason.Length == 0 && category is null) reason = "MissingCategory";
        if (reason.Length == 0 && category is { IsActive: false }) reason = "UnavailableCategory";
        var itemTranslation = product.Descriptions.SingleOrDefault(row => row.Lang == language);
        var name = mapped.Name.Length > 0 ? mapped.Name : itemTranslation?.Name ?? product.Name;
        var variationName = mapped.VariationName ?? (variationId.HasValue ? variation?.Descriptions.SingleOrDefault(row => row.LanguageCode == language)?.Name ?? variation?.Name : null);
        var description = mapped.Description.Length > 0 ? mapped.Description : itemTranslation?.Description ?? string.Empty;
        return new SelectionKey(product.Id, variationId).ToDto(product, primary, language, name, variationName,
            description, mapped.PriceMinor, mapped.Available, reason);
    }

    private static ChannelCatalogueCategoryDto Category(Category category,
        IReadOnlyList<ChannelCatalogueInventoryItemDto> items, string language)
    {
        var rows = items.Where(item => item.CategoryId == category.Id).ToArray();
        var name = category.Translations.SingleOrDefault(row => row.LanguageCode == language)?.Name ?? category.Name;
        var supported = rows.Count(row => row.Supported);
        return new(category.Id, name, category.DisplayOrder, rows.Length, supported, rows.Length - supported, category.IsActive);
    }

    private static string Revision(string provider, string storeId, string currency, bool sandbox, string language,
        Category[] categoryEntities, ChannelCatalogueCategoryDto[] categories, ChannelCatalogueInventoryItemDto[] items)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Provider = provider,
            StoreId = storeId,
            Currency = currency,
            IsSandbox = sandbox,
            Language = language,
            Categories = categories,
            CategorySource = categoryEntities.Select(row => new
            {
                row.Id,
                row.IsActive,
                row.IsDeleted,
                row.Name,
                row.Description,
                row.DisplayOrder,
                row.AvailableOrderTypes,
                Translations = row.Translations.OrderBy(translation => translation.LanguageCode)
                    .Select(translation => new { translation.LanguageCode, translation.Name, translation.Description })
            }),
            Items = Ordered(items)
        })));

    private static ChannelCatalogueInventoryItemDto[] Ordered(IEnumerable<ChannelCatalogueInventoryItemDto> items)
        => items.OrderBy(row => row.CategoryDisplayOrder ?? int.MaxValue).ThenBy(row => row.CategoryId)
            .ThenBy(row => row.ItemDisplayOrder).ThenBy(row => row.ProductId).ThenBy(row => row.VariationId).ToArray();

    private void ValidateBinding(string provider, string storeId, string currency, bool isSandbox, string language)
    {
        ChannelDecisionBinding.Require(provider, storeId, currency, isSandbox, options);
        if (language is not ("en" or "nl" or "fr" or "de" or "tr" or "ar"))
            throw new BadRequestException("Select a supported catalogue language.");
    }

    private sealed record SelectionKey(Guid ProductId, Guid? VariationId)
    {
        public override string ToString() => $"{ProductId:D}:{VariationId?.ToString("D") ?? "base"}";
        public ChannelCatalogueInventoryItemDto ToDto(Product product, ProductCategory? primary, string language,
            string name, string? variationName, string description, int? price, bool available, string reason)
        {
            var category = primary?.Category;
            var categoryName = category?.Translations.SingleOrDefault(row => row.LanguageCode == language)?.Name ?? category?.Name;
            var variation = VariationId is { } id ? product.Variations.Single(row => row.Id == id) : null;
            var fingerprint = Fingerprint(product, variation, category);
            return new(ToString(), ProductId, VariationId, category?.Id, categoryName, category?.DisplayOrder,
                primary?.DisplayOrder ?? product.DisplayOrder, name, description, variationName, price, available,
                reason.Length == 0, reason, fingerprint);
        }

        private static string Fingerprint(Product product, ProductVariation? variation, Category? category)
            => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                product.Id,
                product.Name,
                product.BasePrice,
                product.IsActive,
                product.IsAvailable,
                product.HideBaseProduct,
                product.IsComponent,
                product.DisplayOrder,
                product.AvailableOrderTypes,
                product.SauceMin,
                product.SauceMax,
                product.SauceIncludedFree,
                product.Type,
                Allergens = product.Allergens?.Order(StringComparer.Ordinal).ToArray(),
                Ingredients = product.Ingredients?.Order(StringComparer.Ordinal).ToArray(),
                Descriptions = product.Descriptions.OrderBy(row => row.Lang).Select(row => new { row.Lang, row.Name, row.Description }),
                Variations = product.Variations.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id,
                    row.Name,
                    row.Description,
                    row.PriceModifier,
                    row.IsActive,
                    row.IsDeleted,
                    row.DisplayOrder,
                    Translations = row.Descriptions.OrderBy(text => text.LanguageCode)
                        .Select(text => new { text.LanguageCode, text.Name, text.Description })
                }),
                CustomizationGroups = product.CustomizationGroups.OrderBy(row => row.Id).Select(row => new { row.Id, row.IsActive }),
                DetailedIngredients = product.DetailedIngredients.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id,
                    row.Name,
                    row.IsOptional,
                    row.MaxQuantity,
                    row.Price,
                    row.IsIncludedInBasePrice,
                    row.IsActive,
                    row.DisplayOrder,
                    row.GlobalIngredientId,
                    row.Kind,
                    row.ExclusionGroup
                }),
                Groups = product.CustomizationGroups.OrderBy(row => row.Id).Select(group => new
                {
                    group.Id,
                    group.AuthoringVersion,
                    group.Name,
                    group.Description,
                    group.DisplayOrder,
                    group.IsRequired,
                    group.MinSelection,
                    group.MaxSelection,
                    group.IncludedFreeUnits,
                    group.IsActive,
                    IngredientOptions = group.IngredientOptions.OrderBy(option => option.Id)
                        .Select(option => new { option.Id, option.ProductIngredientId, option.DisplayOrder, option.IsDefault }),
                    ProductOptions = group.ProductOptions.OrderBy(option => option.Id)
                        .Select(option => new { option.Id, option.OptionProductId, option.AdditionalPrice, option.DisplayOrder, option.IsDefault })
                }),
                Category = category is null ? null : new
                {
                    category.Id,
                    category.Name,
                    category.Description,
                    category.IsActive,
                    category.DisplayOrder,
                    category.AvailableOrderTypes
                },
                PrimaryCategoryAssignmentOrder = product.ProductCategories.SingleOrDefault(row => row.IsPrimary)?.DisplayOrder
            })));
    }
}
