using System.Text.Json;
using System.Text.Json.Nodes;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class UberOrderNormalizerTests
{
    private static readonly Guid OrderId = Guid.Parse("2cb52df5-3fb7-4201-bcdf-967427a91000");
    private static readonly Guid ProductId = Guid.Parse("3116d023-b316-4c2b-a3bd-0b251f715000");
    private readonly UberOrderNormalizer _normalizer = new();

    private static TenantStoreBinding Binding() => new()
    {
        StoreId = GatewayFixture.StoreId,
        Currency = "EUR",
        CatalogueRevision = "approved-v1",
        PublishedMenuHash = new string('a', 64),
        Items = [new() { ProviderItemId = "published-meal-v1", ProductId = ProductId }],
    };

    private static JsonNode Order() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/uber-order-simple.json")))!;
    private TenantOrderRequest Normalize(JsonNode order, TenantStoreBinding? binding = null)
        => _normalizer.Normalize(JsonSerializer.SerializeToElement(order), OrderId, binding ?? Binding());

    [Fact]
    public void CapturedSimpleShapePreservesProviderMoneyIdentityAndInstructions()
    {
        // Amount oracle is the captured sandbox contract: 500 minor EUR units, independent of production conversion code.
        var result = Normalize(Order());
        Assert.Equal(5m, result.MerchantTotal); Assert.Null(result.ReportedTax);
        Assert.Equal("EUR", result.Currency); Assert.Equal(OrderId.ToString(), result.ExternalOrderId);
        Assert.Equal(GatewayFixture.StoreId.ToString(), result.StoreId);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T19:25:07+02:00"), result.PlacedAt);
        var item = Assert.Single(result.Items);
        Assert.Equal(ProductId, item.ProductId); Assert.Equal(5m, item.UnitPrice); Assert.Equal(5m, item.Total);
        Assert.Equal("No peanuts; severe allergy.\nKeep this instruction.", item.Instructions);
        Assert.Equal("SANDBOX TEST ONLY: do not prepare food or dispatch a courier.", result.Instructions);
        Assert.Matches("^[a-f0-9]{64}$", result.CanonicalOrderHash);
    }

    [Fact]
    public void AnonymizedPhoneAccessCodeIsPreservedSeparatelyFromNotes()
    {
        var order = Order();
        order["eater"]!["phone"] = "+31 200000000";
        order["eater"]!["phone_code"] = "555 55 555";
        var result = Normalize(order);
        Assert.Equal("+31 200000000", result.CustomerPhone);
        Assert.Equal("555 55 555", result.CustomerPhoneAccessCode);
        Assert.Equal("SANDBOX TEST ONLY: do not prepare food or dispatch a courier.", result.Instructions);
    }

    [Theory]
    [InlineData("", "12345")]
    [InlineData("+31 200000000", "wrong")]
    [InlineData("+31 200000000", " ")]
    [InlineData("+31 200000000", "123\u001b45")]
    [InlineData("+31 200000000", "1234567890123456789012345678901")]
    public void InvalidPhoneAccessCodeCannotBeForwarded(string phone, string code)
    {
        var order = Order(); order["eater"]!["phone"] = phone; order["eater"]!["phone_code"] = code;
        Assert.Throws<ChannelConsoleException>(() => Normalize(order));
    }

    [Fact]
    public void ReportedZeroTaxRemainsDistinctFromMissingTax()
    {
        var order = Order(); order["payment"]!["charges"]!["tax"] = JsonNode.Parse("{\"amount\":0,\"currency_code\":\"EUR\"}");
        Assert.Equal(0m, Normalize(order).ReportedTax);
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("missing-money")]
    [InlineData("string-money")]
    [InlineData("fractional-money")]
    [InlineData("negative-money")]
    [InlineData("checkout-total")]
    [InlineData("item-total")]
    [InlineData("cash")]
    [InlineData("promotions")]
    [InlineData("tax-excess")]
    [InlineData("null-item")]
    [InlineData("primitive-item")]
    [InlineData("modifiers")]
    [InlineData("requests")]
    [InlineData("fulfillment-issues")]
    [InlineData("foreign-store")]
    [InlineData("foreign-order")]
    [InlineData("terminal")]
    [InlineData("restaurant-delivery")]
    [InlineData("missing-date")]
    [InlineData("printer-control")]
    [InlineData("oversized-note")]
    [InlineData("unknown-item")]
    [InlineData("ambiguous-map")]
    [InlineData("quantity-string")]
    [InlineData("quantity-zero")]
    [InlineData("packaging")]
    [InlineData("phone-code")]
    public void UnsupportedEvidenceStaysHeld(string mutation)
    {
        var order = Order(); var binding = Binding(); var charges = order["payment"]!["charges"]!;
        var item = order["cart"]!["items"]![0]!;
        switch (mutation)
        {
            case "currency": charges["total"]!["currency_code"] = "CHF"; break;
            case "missing-money": charges["total"]!.AsObject().Remove("amount"); break;
            case "string-money": charges["total"]!["amount"] = "500"; break;
            case "fractional-money": charges["total"]!["amount"] = 500.5m; break;
            case "negative-money": charges["total"]!["amount"] = -1; break;
            case "checkout-total": charges["total"]!["amount"] = 1147; break;
            case "item-total": item["price"]!["total_price"]!["amount"] = 499; break;
            case "cash": charges["cash_amount_due"] = JsonNode.Parse("{\"amount\":500,\"currency_code\":\"EUR\"}"); break;
            case "tax-excess": charges["tax"] = JsonNode.Parse("{\"amount\":501,\"currency_code\":\"EUR\"}"); break;
            case "promotions": order["payment"]!["promotions"] = JsonNode.Parse("[{}]"); break;
            case "null-item": order["cart"]!["items"]![0] = null; break;
            case "primitive-item": order["cart"]!["items"]![0] = "unexpected"; break;
            case "modifiers": item["selected_modifier_groups"] = JsonNode.Parse("[{}]"); break;
            case "requests": item["special_requests"] = JsonNode.Parse("[{}]"); break;
            case "fulfillment-issues": order["cart"]!["fulfillment_issues"] = JsonNode.Parse("[{}]"); break;
            case "foreign-store": order["store"]!["id"] = Guid.NewGuid().ToString(); break;
            case "foreign-order": order["id"] = Guid.NewGuid().ToString(); break;
            case "terminal": order["current_state"] = "DENIED"; break;
            case "restaurant-delivery": order["type"] = "DELIVERY_BY_RESTAURANT"; break;
            case "missing-date": order.AsObject().Remove("placed_at"); break;
            case "printer-control": item["special_instructions"] = "No peanuts\u001b@"; break;
            case "oversized-note": item["special_instructions"] = new string('x', 501); break;
            case "unknown-item": item["id"] = "unmapped-item"; break;
            case "ambiguous-map": binding.Items.Add(binding.Items[0]); break;
            case "quantity-string": item["quantity"] = "1"; break;
            case "quantity-zero": item["quantity"] = 0; break;
            case "packaging": order["packaging"] = JsonNode.Parse("{\"disposable_items\":{}}"); break;
            case "phone-code": order["eater"]!["phone_code"] = "123\u001b45"; break;
        }
        Assert.Equal(422, Assert.Throws<ChannelConsoleException>(() => Normalize(order, binding)).Status);
    }
}
