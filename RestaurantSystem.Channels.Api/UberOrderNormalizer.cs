using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.Channels.Api;

/// <summary>Canonical evidence to the tenant wire. Unsupported structures are refused, never flattened.</summary>
public sealed class UberOrderNormalizer : IUberOrderNormalizer
{
    public TenantOrderRequest Normalize(JsonElement order, Guid expectedOrderId, TenantStoreBinding binding)
    {
        if (order.ValueKind != JsonValueKind.Object || expectedOrderId == Guid.Empty) throw UberOrderValue.Unsupported();
        if (binding.Currency is not ("EUR" or "CHF") || binding.StoreId == Guid.Empty
            || string.IsNullOrWhiteSpace(binding.CatalogueRevision) || binding.PublishedMenuHash.Length != 64 || !binding.PublishedMenuHash.All(Uri.IsHexDigit)
            || binding.Items.Count == 0 || binding.Items.Any(item => item.ProductId == Guid.Empty)
            || binding.Items.GroupBy(item => item.ProviderItemId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw UberOrderValue.Unsupported();
        if (!Guid.TryParse(UberOrderValue.Text(order, "id", 36), out var orderId) || orderId != expectedOrderId
            || !Guid.TryParse(UberOrderValue.Text(UberOrderValue.Object(order, "store"), "id", 36), out var storeId)
            || storeId != binding.StoreId || UberOrderValue.Text(order, "current_state", 24) != "CREATED")
            throw UberOrderValue.Unsupported();
        var type = UberOrderValue.Text(order, "type", 30);
        if (type != "DELIVERY_BY_UBER") throw UberOrderValue.Unsupported();
        if (!DateTimeOffset.TryParse(UberOrderValue.Text(order, "placed_at", 40), CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var placedAt) || placedAt == default) throw UberOrderValue.Unsupported();
        var cart = UberOrderValue.Object(order, "cart");
        if (UberOrderValue.HasContent(cart, "fulfillment_issues")
            || !cart.TryGetProperty("items", out var sourceItems) || sourceItems.ValueKind != JsonValueKind.Array
            || sourceItems.GetArrayLength() is < 1 or > 200) throw UberOrderValue.Unsupported();
        var items = sourceItems.EnumerateArray().Select(item => Item(item, binding)).ToArray();
        var payment = UberOrderValue.Object(order, "payment");
        if (UberOrderValue.HasContent(payment, "promotions")) throw UberOrderValue.Unsupported();
        var charges = UberOrderValue.Object(payment, "charges");
        var total = UberOrderValue.Money(charges, "total", binding.Currency);
        var subtotal = UberOrderValue.Money(charges, "sub_total", binding.Currency);
        if (total != subtotal || subtotal != items.Sum(item => item.Total)) throw UberOrderValue.Unsupported();
        decimal? tax = charges.TryGetProperty("tax", out var taxValue) && taxValue.ValueKind != JsonValueKind.Null
            ? UberOrderValue.Money(charges, "tax", binding.Currency) : null;
        if (tax > total) throw UberOrderValue.Unsupported();
        foreach (var charge in charges.EnumerateObject().Where(property => property.Name is not ("total" or "sub_total" or "tax")))
            if (charge.Value.ValueKind != JsonValueKind.Null && UberOrderValue.Money(charges, charge.Name, binding.Currency) != 0)
                throw UberOrderValue.Unsupported();
        var eater = UberOrderValue.Object(order, "eater");
        if (UberOrderValue.HasContent(order, "packaging") || !string.IsNullOrEmpty(Optional(eater, "phone_code", 30)))
            throw UberOrderValue.Unsupported();
        // Do not retain the provider's private eater UUID, address, courier or tax-profile objects.
        return new("uber-eats", binding.StoreId.ToString("D"), orderId.ToString("D"),
            UberOrderValue.Text(order, "display_id", 100),
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(order.GetRawText()))),
            binding.Currency, total, tax, placedAt, type,
            Optional(eater, "first_name", 100), Optional(eater, "phone", 20),
            Optional(cart, "special_instructions", 1000, notes: true), items);
    }

    private static TenantOrderItem Item(JsonElement item, TenantStoreBinding binding)
    {
        if (UberOrderValue.HasContent(item, "selected_modifier_groups") || UberOrderValue.HasContent(item, "special_requests"))
            throw UberOrderValue.Unsupported();
        var itemId = UberOrderValue.Text(item, "id", 128);
        var mapping = binding.Items.SingleOrDefault(mapping => mapping.ProviderItemId == itemId)
            ?? throw UberOrderValue.Unsupported();
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("quantity", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var quantity) || quantity is < 1 or > 100)
            throw UberOrderValue.Unsupported();
        var price = UberOrderValue.Object(item, "price");
        var unit = UberOrderValue.Money(price, "unit_price", binding.Currency);
        var total = UberOrderValue.Money(price, "total_price", binding.Currency);
        if (unit * quantity != total || UberOrderValue.Money(price, "base_unit_price", binding.Currency) != unit
            || UberOrderValue.Money(price, "base_total_price", binding.Currency) != total) throw UberOrderValue.Unsupported();
        if (price.TryGetProperty("base_non_loyalty_unit_price", out var loyalty) && loyalty.ValueKind != JsonValueKind.Null
            && UberOrderValue.Money(price, "base_non_loyalty_unit_price", binding.Currency) != unit)
            throw UberOrderValue.Unsupported();
        return new(mapping.ProductId, mapping.VariationId, UberOrderValue.Text(item, "title", 100), mapping.VariationName,
            quantity, unit, total, Optional(item, "special_instructions", 500, notes: true));
    }

    private static string? Optional(JsonElement value, string key, int length, bool notes = false)
    {
        var text = UberOrderValue.Text(value, key, length, optional: true, notes);
        return text.Length == 0 ? null : text;
    }
}
