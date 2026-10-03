using System.Security.Cryptography;
using System.Text.Json;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Services;

internal static class BasketPurchaseFingerprint
{
    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        PropertyNamingPolicy = null,
    };

    internal static string Compute(
        OrderType? orderType, IReadOnlyList<BasketItemDto> basketItems, IBasketToOrderTranslator translator)
    {
        ArgumentNullException.ThrowIfNull(basketItems);
        ArgumentNullException.ThrowIfNull(translator);

        var items = translator.Translate(basketItems)
            .Select(ToSnapshot)
            .Select(item => (Snapshot: item, Json: JsonSerializer.Serialize(item, CanonicalJson)))
            .OrderBy(item => item.Json, StringComparer.Ordinal)
            .Select(item => item.Snapshot)
            .ToArray();
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new BasketSnapshot(orderType, items), CanonicalJson);
        return Convert.ToHexString(SHA256.HashData(canonical));
    }

    internal static bool IsValidDigest(string? value) => value is { Length: 64 }
        && value.All(Uri.IsHexDigit);

    internal static bool Matches(string? expected, string? actual)
    {
        if (!IsValidDigest(expected) || !IsValidDigest(actual))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expected!), Convert.FromHexString(actual!));
    }

    private static PurchaseItemSnapshot ToSnapshot(CreateOrderItemDto item)
    {
        var children = item.ChildItems?.Select(ToSnapshot)
            .Select(child => (Snapshot: child, Json: JsonSerializer.Serialize(child, CanonicalJson)))
            .OrderBy(child => child.Json, StringComparer.Ordinal)
            .Select(child => child.Snapshot)
            .ToArray();
        var ingredientQuantities = item.IngredientQuantities?.OrderBy(pair => pair.Key)
            .Select(pair => new IngredientQuantity(pair.Key, pair.Value))
            .ToArray();
        var selectedIngredients = item.SelectedIngredientIds?.Order().ToArray();
        var customizations = item.CustomizationSelections?.Select(group => new CustomizationGroup(
                group.GroupId,
                group.Options.OrderBy(option => option.Kind)
                    .ThenBy(option => option.OptionId)
                    .ThenBy(option => option.Quantity)
                    .Select(option => new CustomizationOption(option.Kind, option.OptionId, option.Quantity))
                    .ToArray()))
            .OrderBy(group => group.GroupId)
            .ToArray();

        return new PurchaseItemSnapshot(
            item.ProductId, item.ProductVariationId, item.MenuId, item.Quantity,
            item.UnitPrice, item.CustomizationPrice, item.SpecialInstructions,
            ingredientQuantities, selectedIngredients, customizations, item.SectionId, item.Kind, children);
    }

    private sealed record BasketSnapshot(OrderType? OrderType, IReadOnlyList<PurchaseItemSnapshot> Items);

    private sealed record PurchaseItemSnapshot(
        Guid? ProductId,
        Guid? ProductVariationId,
        Guid? MenuId,
        int Quantity,
        decimal UnitPrice,
        decimal CustomizationPrice,
        string? SpecialInstructions,
        IReadOnlyList<IngredientQuantity>? IngredientQuantities,
        IReadOnlyList<Guid>? SelectedIngredientIds,
        IReadOnlyList<CustomizationGroup>? CustomizationSelections,
        Guid? SectionId,
        OrderItemKind? Kind,
        IReadOnlyList<PurchaseItemSnapshot>? ChildItems);

    private sealed record IngredientQuantity(Guid IngredientId, int Quantity);

    private sealed record CustomizationGroup(Guid GroupId, IReadOnlyList<CustomizationOption> Options);

    private sealed record CustomizationOption(
        RestaurantSystem.Domain.Common.Enums.CustomizationOptionKind Kind, Guid OptionId, int Quantity);
}
