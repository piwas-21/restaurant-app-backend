using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    internal static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException($"The persisted {typeof(T).Name} snapshot is missing.");

    internal static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    internal static string ChangeFingerprint(IEnumerable<OrderAmendmentChangeSnapshot> changes) =>
        Hash(Serialize(changes.Select(change => change with
        {
            // The daily order number is assigned at commit, after the quote was reviewed.
            ReplacementDispatchedOrderNumber = null
        }).ToList()));

    internal static string PricingFingerprint(OrderDto order)
    {
        var snapshot = new PricedSupplementSnapshot(
            order.Type,
            order.Currency,
            order.SubTotal,
            order.Tax,
            order.DeliveryFee,
            order.Discount,
            order.DiscountPercentage,
            order.CustomerDiscountAmount,
            order.FidelityPointsDiscount,
            order.FidelityPointsEarned,
            order.FidelityPointsRedeemed,
            order.Tip,
            order.Total,
            order.Items.Select(ToPricedItem).ToList());
        return Hash(Serialize(snapshot));
    }

    internal static void EnsureItemIdentity(Order order)
    {
        foreach (var item in order.Items)
        {
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }

            if (item.ParentOrderItem is not null)
            {
                item.ParentOrderItemId = item.ParentOrderItem.Id;
            }
        }
    }

    private static PricedItemSnapshot ToPricedItem(OrderItemDto item) => new(
        item.ProductId,
        item.ProductVariationId,
        item.MenuID,
        item.ProductName,
        item.VariationName,
        item.Quantity,
        item.UnitPrice,
        item.ItemTotal,
        item.SpecialInstructions,
        item.KitchenType,
        item.IngredientCustomizations?.Select(ingredient => new PricedIngredientSnapshot(
            ingredient.IngredientId,
            ingredient.IngredientName,
            ingredient.Quantity,
            ingredient.IsRemoved,
            ingredient.IsAddOn)).ToList() ?? [],
        item.SideItems?.Select(ToPricedItem).ToList() ?? []);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record PricedSupplementSnapshot(
        string Type,
        string? Currency,
        decimal SubTotal,
        decimal Tax,
        decimal DeliveryFee,
        decimal Discount,
        decimal DiscountPercentage,
        decimal CustomerDiscountAmount,
        decimal FidelityPointsDiscount,
        int FidelityPointsEarned,
        int FidelityPointsRedeemed,
        decimal Tip,
        decimal Total,
        IReadOnlyList<PricedItemSnapshot> Items);

    private sealed record PricedItemSnapshot(
        Guid? ProductId,
        Guid? ProductVariationId,
        Guid? MenuId,
        string ProductName,
        string? VariationName,
        int Quantity,
        decimal UnitPrice,
        decimal ItemTotal,
        string? SpecialInstructions,
        string? KitchenType,
        IReadOnlyList<PricedIngredientSnapshot> Ingredients,
        IReadOnlyList<PricedItemSnapshot> Children);

    private sealed record PricedIngredientSnapshot(
        Guid IngredientId,
        string IngredientName,
        int Quantity,
        bool IsRemoved,
        bool IsAddOn);
}
