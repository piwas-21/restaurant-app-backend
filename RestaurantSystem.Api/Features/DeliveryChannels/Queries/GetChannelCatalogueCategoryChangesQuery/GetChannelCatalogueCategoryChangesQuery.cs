using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCategoriesQuery;
using RestaurantSystem.Api.Features.DeliveryChannels.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCategoryChangesQuery;

public sealed record GetChannelCatalogueCategoryChangesQuery(ChannelCatalogueCategoryReferencesRequest Request)
    : IQuery<ChannelCatalogueCategoriesSnapshot>;

public sealed class GetChannelCatalogueCategoryChangesQueryHandler(IChannelCatalogueInventoryReader inventory,
    IOptions<DeliveryChannelSettings> options, IEmailLanguageResolver languages)
    : IQueryHandler<GetChannelCatalogueCategoryChangesQuery, ChannelCatalogueCategoriesSnapshot>
{
    public async Task<ChannelCatalogueCategoriesSnapshot> Handle(GetChannelCatalogueCategoryChangesQuery query,
        CancellationToken cancellationToken)
    {
        var request = query.Request;
        Validate(request);
        var binding = ChannelCatalogueBinding.Require(options.Value);
        var source = await inventory.Read(binding.Provider, binding.StoreId, binding.Currency, true,
            languages.TenantDefault, cancellationToken);
        var categories = source.Categories.Select(row => row.CategoryId).ToHashSet();
        var items = source.Items.ToDictionary(row => (row.ProductId, row.VariationId));
        var removedCategories = request.CategoryIds.Where(id => !categories.Contains(id)).Distinct().Order().ToArray();
        var removedItems = request.ItemReferences.Select(row => Changed(row, items, categories))
            .Where(row => row is not null).Select(row => row!).OrderBy(row => row.CategoryId)
            .ThenBy(row => row.ProductId).ThenBy(row => row.VariationId).ToArray();
        var removedOverrides = request.ItemOverrides.Select(row => Changed(row, items, categories))
            .Where(row => row is not null).Select(row => row!).OrderBy(row => row.CategoryId)
            .ThenBy(row => row.ProductId).ThenBy(row => row.VariationId).ToArray();
        var itemStatuses = ItemStatusRequests(request).Select(row => Status(row, items))
            .Where(row => row is not null).Select(row => row!).OrderBy(row => row.CategoryId)
            .ThenBy(row => row.ProductId).ThenBy(row => row.VariationId).ToArray();
        return new(source.Provider, source.StoreId, source.Currency, source.IsSandbox, source.Language,
            source.Revision, source.Categories)
        {
            SourceChanged = request.ExpectedSourceRevision.Length > 0 && request.ExpectedSourceRevision != source.Revision,
            RemovedCategoryIds = removedCategories,
            RemovedItems = removedItems,
            RemovedItemOverrides = removedOverrides,
            ItemStatuses = itemStatuses
        };
    }

    private static ChannelCatalogueItemStatusDto? Status(ChannelCatalogueItemReferenceRequest row,
        Dictionary<(Guid ProductId, Guid? VariationId), ChannelCatalogueInventoryItemDto> items)
        => items.TryGetValue((row.ProductId, row.VariationId), out var current)
            ? new($"{row.ProductId:D}:{row.VariationId?.ToString("D") ?? "base"}", row.ProductId,
                row.VariationId, row.CategoryId!.Value, current.CategoryId, current.Supported)
            : null;

    private static ChannelCatalogueItemReferenceRequest[] ItemStatusRequests(ChannelCatalogueCategoryReferencesRequest request)
        => request.ItemReferences.Concat(request.ItemOverrides.Select(row => new ChannelCatalogueItemReferenceRequest(
                row.ProductId, row.VariationId, row.CategoryId)))
            .GroupBy(row => (row.ProductId, row.VariationId)).Select(group => group.First()).ToArray();

    private static ChannelCatalogueRemovedItemReferenceDto? Changed(ChannelCatalogueItemReferenceRequest row,
        Dictionary<(Guid ProductId, Guid? VariationId), ChannelCatalogueInventoryItemDto> items, HashSet<Guid> categories)
    {
        if (!items.TryGetValue((row.ProductId, row.VariationId), out var current))
            return RemovedReference(row, null, "itemRemoved");
        if (row.CategoryId == current.CategoryId) return null;
        return RemovedReference(row, current.CategoryId, categories.Contains(row.CategoryId!.Value) ? "categoryChanged" : "categoryRemoved");
    }

    private static ChannelCatalogueRemovedItemReferenceDto RemovedReference(ChannelCatalogueItemReferenceRequest row,
        Guid? currentCategoryId, string reason)
        => new($"{row.ProductId:D}:{row.VariationId?.ToString("D") ?? "base"}", row.ProductId,
            row.VariationId, row.CategoryId!.Value, currentCategoryId, reason);

    private static ChannelCatalogueRemovedItemOverrideDto? Changed(ChannelCatalogueItemOverrideRequest row,
        Dictionary<(Guid ProductId, Guid? VariationId), ChannelCatalogueInventoryItemDto> items, HashSet<Guid> categories)
    {
        if (!items.TryGetValue((row.ProductId, row.VariationId), out var current))
            return Removed(row, null, "itemRemoved");
        if (row.CategoryId == current.CategoryId) return null;
        return Removed(row, current.CategoryId, categories.Contains(row.CategoryId!.Value) ? "categoryChanged" : "categoryRemoved");
    }

    private static ChannelCatalogueRemovedItemOverrideDto Removed(ChannelCatalogueItemOverrideRequest row,
        Guid? currentCategoryId, string reason)
        => new($"{row.ProductId:D}:{row.VariationId?.ToString("D") ?? "base"}", row.ProductId,
            row.VariationId, row.CategoryId!.Value, currentCategoryId, reason);

    private static void Validate(ChannelCatalogueCategoryReferencesRequest request)
    {
        if (request is not null && request.CategoryIds?.Count > ExternalOrderLimits.MaximumCatalogueCategories)
            throw new BadRequestException("Review no more than 1,000 categories at a time.", "CategoryLimitExceeded");
        if (request is not null && request.ItemOverrides?.Count > ExternalOrderLimits.MaximumCatalogueItemOverrides)
            throw new BadRequestException("Review no more than 2,000 individual item overrides at a time.", "SelectionOverrideLimitExceeded");
        if (request is null || request.CategoryIds is null || request.ItemReferences is null || request.ItemOverrides is null
            || request.CategoryIds.Any(id => id == Guid.Empty)
            || request.CategoryIds.Distinct().Count() != request.CategoryIds.Count
            || request.ItemReferences.Count > ExternalOrderLimits.MaxItems
            || request.ItemReferences.Any(row => row is null || row.ProductId == Guid.Empty || row.VariationId == Guid.Empty || row.CategoryId is null || row.CategoryId == Guid.Empty)
            || request.ItemReferences.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != request.ItemReferences.Count
            || request.ItemOverrides.Any(row => row is null || row.ProductId == Guid.Empty || row.VariationId == Guid.Empty
                || row.CategoryId is null || row.CategoryId == Guid.Empty)
            || request.ItemOverrides.Select(row => (row.ProductId, row.VariationId)).Distinct().Count() != request.ItemOverrides.Count
            || request.ItemReferences.Concat(request.ItemOverrides.Select(row => new ChannelCatalogueItemReferenceRequest(
                    row.ProductId, row.VariationId, row.CategoryId)))
                .GroupBy(row => (row.ProductId, row.VariationId)).Any(group => group.Select(row => row.CategoryId).Distinct().Count() > 1)
            || request.ExpectedSourceRevision is null || request.ExpectedSourceRevision.Length is not (0 or 64)
            || request.ExpectedSourceRevision.Length > 0 && request.ExpectedSourceRevision.Any(c => !char.IsAsciiHexDigitLower(c)))
            throw new BadRequestException("Refresh the tenant catalogue and review the selected categories and products.");
    }
}
