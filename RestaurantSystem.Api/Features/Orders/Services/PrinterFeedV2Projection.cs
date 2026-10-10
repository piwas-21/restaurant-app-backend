using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Builds the opt-in printer projection without changing legacy feed quantities.</summary>
internal static class PrinterFeedV2Projection
{
    internal static void Apply(IReadOnlyList<OrderDto> orders, IReadOnlyCollection<Order> sources)
    {
        var sourceById = sources.ToDictionary(order => order.Id);
        foreach (var orderDto in orders)
        {
            if (!sourceById.TryGetValue(orderDto.Id, out var source)) continue;
            var rowsById = source.Items.ToDictionary(item => item.Id);
            foreach (var item in orderDto.Items)
                ApplyCurrentItem(item, rowsById, isRoot: true);
        }
    }

    internal static void NormalizeUpdates(IReadOnlyList<PrinterFeedUpdateDto> updates)
    {
        foreach (var update in updates)
            foreach (var change in update.Changes)
            {
                if (change.Previous is not null) NormalizeSnapshotItem(change.Previous, isRoot: true);
                if (change.Current is not null) NormalizeSnapshotItem(change.Current, isRoot: true);
            }
    }

    private static void ApplyCurrentItem(OrderItemDto dto, IReadOnlyDictionary<Guid, OrderItem> rowsById, bool isRoot)
    {
        if (!rowsById.TryGetValue(dto.Id, out var row))
        {
            NormalizeSnapshotItem(dto, isRoot);
            return;
        }

        dto.Quantity = row.Quantity;
        dto.QuantityBasis = row.QuantityBasis ?? (isRoot ? QuantityBasis.LineTotal : QuantityBasis.Unknown);
        dto.ConfigurationScope = row.ConfigurationScope ?? ConfigurationScope.Unknown;
        dto.CompositionRole = row.CompositionRole ?? CompositionRole.Unknown;
        dto.MenuSectionItemId = row.MenuSectionItemId;
        dto.SuggestedSideItemId = row.SuggestedSideItemId;
        dto.ParentComponentOrderItemId = row.ParentComponentOrderItemId;
        dto.PresentationLabel = row.PresentationLabel;
        dto.PresentationOrder = row.PresentationOrder;

        var ingredientsById = row.IngredientSnapshots
            .OrderBy(ingredient => ingredient.SortOrder)
            .GroupBy(ingredient => ingredient.IngredientId)
            .ToDictionary(group => group.Key, group => new Queue<OrderItemIngredient>(group));
        foreach (var ingredient in dto.IngredientCustomizations ?? [])
        {
            if (ingredientsById.TryGetValue(ingredient.IngredientId, out var occurrences)
                && occurrences.TryDequeue(out var frozen))
            {
                ingredient.QuantityBasis = frozen.QuantityBasis ?? QuantityBasis.Unknown;
                ingredient.ConfigurationScope = frozen.ConfigurationScope ?? ConfigurationScope.Unknown;
                ingredient.CompositionRole = frozen.CompositionRole ?? CompositionRole.Unknown;
                ingredient.PresentationOrder = frozen.PresentationOrder;
            }
            else
            {
                NormalizeSnapshotIngredient(ingredient);
            }
        }

        foreach (var child in dto.SideItems ?? [])
            ApplyCurrentItem(child, rowsById, isRoot: false);
    }

    private static void NormalizeSnapshotItem(OrderItemDto item, bool isRoot)
    {
        item.QuantityBasis ??= isRoot ? QuantityBasis.LineTotal : QuantityBasis.Unknown;
        item.ConfigurationScope ??= ConfigurationScope.Unknown;
        item.CompositionRole ??= CompositionRole.Unknown;
        foreach (var ingredient in item.IngredientCustomizations ?? [])
            NormalizeSnapshotIngredient(ingredient);
        foreach (var child in item.SideItems ?? [])
            NormalizeSnapshotItem(child, isRoot: false);
    }

    private static void NormalizeSnapshotIngredient(OrderItemIngredientDto ingredient)
    {
        ingredient.QuantityBasis ??= QuantityBasis.Unknown;
        ingredient.ConfigurationScope ??= ConfigurationScope.Unknown;
        ingredient.CompositionRole ??= CompositionRole.Unknown;
    }
}
