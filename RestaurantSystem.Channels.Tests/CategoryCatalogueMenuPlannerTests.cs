using System.Text.Json;
using System.Text.Json.Nodes;
using RestaurantSystem.Channels.Api;

namespace RestaurantSystem.Channels.Tests;

public sealed class CategoryCatalogueMenuPlannerTests
{
    private static readonly Guid MealsCategoryId = Guid.Parse("41000000-0000-0000-0000-000000000001");
    private static readonly Guid DrinksCategoryId = Guid.Parse("41000000-0000-0000-0000-000000000002");
    private static readonly Guid SoupProductId = Guid.Parse("42000000-0000-0000-0000-000000000001");
    private static readonly Guid LargeVariationId = Guid.Parse("43000000-0000-0000-0000-000000000001");
    private static readonly Guid TeaProductId = Guid.Parse("42000000-0000-0000-0000-000000000002");
    private static readonly string SourceRevision = new('a', 64);

    [Fact]
    public void BuildProjectsStableIdentitiesCategoriesContentAndAvailability()
    {
        var fixture = CreateFixture();
        var plan = CatalogueMenuPlanner.Build(fixture.Template, fixture.Store, fixture.Source);

        Assert.True(plan.CanPublish);
        Assert.Equal(3, plan.Items.Count);
        var expectedItemIds = new[]
        {
            ItemId(SoupProductId, null), ItemId(SoupProductId, LargeVariationId), ItemId(TeaProductId, null)
        };
        var outputItems = plan.Menu.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(expectedItemIds, outputItems.Select(row => row.GetProperty("id").GetString()));
        Assert.Equal(expectedItemIds.Length, outputItems.Select(row => row.GetProperty("id").GetString()).Distinct().Count());

        Assert.Equal("Vegetable soup", outputItems[0].GetProperty("title").GetProperty("translations").GetProperty("en_us").GetString());
        Assert.Equal("Soup description", outputItems[0].GetProperty("description").GetProperty("translations").GetProperty("en_us").GetString());
        Assert.Equal("Large vegetable soup", outputItems[1].GetProperty("title").GetProperty("translations").GetProperty("en_us").GetString());
        Assert.Equal("Tea", outputItems[2].GetProperty("title").GetProperty("translations").GetProperty("en_us").GetString());
        Assert.Equal(new[] { 1_250, 1_500, 300 }, outputItems.Select(row => row.GetProperty("price_info").GetProperty("price").GetInt32()));
        Assert.All(outputItems, row =>
        {
            Assert.Equal("EUR", row.GetProperty("price_info").GetProperty("currency").GetString());
            Assert.Equal(100, row.GetProperty("price_info").GetProperty("sale_price").GetInt32());
            Assert.True(JsonElement.DeepEquals(fixture.Template.GetProperty("items")[0].GetProperty("tax_info"),
                row.GetProperty("tax_info")));
        });
        Assert.Equal(JsonValueKind.Null, outputItems[0].GetProperty("suspension_info").GetProperty("suspension").ValueKind);
        Assert.Equal("Unavailable in Sofra", outputItems[1].GetProperty("suspension_info").GetProperty("suspension")
            .GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, outputItems[2].GetProperty("suspension_info").GetProperty("suspension").ValueKind);

        var expectedCategoryIds = new[] { CategoryId(MealsCategoryId), CategoryId(DrinksCategoryId) };
        var outputMenus = plan.Menu.GetProperty("menus").EnumerateArray().ToArray();
        var templateMenus = fixture.Template.GetProperty("menus").EnumerateArray().ToArray();
        Assert.Equal(2, outputMenus.Length);
        for (var index = 0; index < outputMenus.Length; index++)
        {
            Assert.Equal(expectedCategoryIds, outputMenus[index].GetProperty("category_ids")
                .EnumerateArray().Select(row => row.GetString()));
            Assert.True(JsonElement.DeepEquals(templateMenus[index].GetProperty("service_availability"),
                outputMenus[index].GetProperty("service_availability")));
        }

        var outputCategories = plan.Menu.GetProperty("categories").EnumerateArray().ToArray();
        Assert.Equal(expectedCategoryIds, outputCategories.Select(row => row.GetProperty("id").GetString()));
        Assert.Equal(new[] { ItemId(SoupProductId, null), ItemId(SoupProductId, LargeVariationId) },
            outputCategories[0].GetProperty("entities").EnumerateArray().Select(row => row.GetProperty("id").GetString()));
        Assert.Equal(new[] { ItemId(TeaProductId, null) },
            outputCategories[1].GetProperty("entities").EnumerateArray().Select(row => row.GetProperty("id").GetString()));
        Assert.DoesNotContain(outputItems, row => row.GetProperty("id").GetString()!.StartsWith("template-item", StringComparison.Ordinal));
        Assert.Equal(ProviderJson.Hash(fixture.Template.GetProperty("items")[0].GetProperty("tax_info")),
            plan.TaxProfileRevision);
    }

    [Fact]
    public void BuildKeepsUnsupportedItemsAsPublicationBlockers()
    {
        var fixture = CreateFixture();
        var unsupportedVariation = fixture.Source.Items[1] with
        { PriceMinor = null, Available = false, BlockReason = "UnmappedAllergens" };
        var source = fixture.Source with
        {
            Items = [fixture.Source.Items[0], unsupportedVariation, fixture.Source.Items[2]],
            Categories =
            [
                new(MealsCategoryId, "Meals", 0, 2, 1, 1, 2, 1, true),
                new(DrinksCategoryId, "Drinks", 1, 1, 1, 0, 1, 0, true)
            ]
        };
        fixture.Store.Categories[0].SupportedItemCount = 1;
        fixture.Store.Categories[0].UnsupportedItemCount = 1;
        fixture.Store.Categories[0].SelectedUnsupportedItemCount = 1;

        var plan = CatalogueMenuPlanner.Build(fixture.Template, fixture.Store, source);

        Assert.False(plan.CanPublish);
        var blocked = Assert.Single(plan.Items, item => item.VariationId == LargeVariationId);
        Assert.Equal("UnmappedAllergens", blocked.BlockReason);
        Assert.Null(blocked.PriceMinor);
    }

    [Theory]
    [InlineData("different-tax", "ReviewedTaxProfileUnavailable")]
    [InlineData("invalid-tax", "ReviewedTaxProfileUnavailable")]
    [InlineData("price-metadata", "ReviewedTemplateIncompatible")]
    [InlineData("invalid-price", "ReviewedTemplateIncompatible")]
    [InlineData("item-modifiers", "ReviewedTemplateIncompatible")]
    [InlineData("global-modifiers", "ReviewedTemplateIncompatible")]
    public void BuildBlocksTaxAmbiguityAndUnsupportedTemplateTopology(string change, string expectedReason)
    {
        var fixture = CreateFixture();
        var template = ChangedTemplate(fixture.Template, change);

        var plan = CatalogueMenuPlanner.Build(template, fixture.Store, fixture.Source);

        Assert.False(plan.CanPublish);
        Assert.All(plan.Items, item => Assert.Equal(expectedReason, item.BlockReason));
    }

    private static Fixture CreateFixture()
    {
        var rows = new[]
        {
            Mapping(SoupProductId, null, MealsCategoryId, "Meals", 0, 0, "Vegetable soup", null,
                "Soup description", 1_250, true),
            Mapping(SoupProductId, LargeVariationId, MealsCategoryId, "Meals", 0, 1, "Large vegetable soup", "Large",
                "Large soup description", 1_500, false),
            Mapping(TeaProductId, null, DrinksCategoryId, "Drinks", 1, 0, "Tea", null,
                "Tea description", 300, true)
        };
        var store = new TenantStoreBinding
        {
            StoreId = Guid.Parse("44000000-0000-0000-0000-000000000001"),
            TenantId = "category-planner-test",
            BaseUrl = "https://tenant.example/",
            Currency = "EUR",
            CatalogueRevision = new string('b', 64),
            PublishedMenuHash = new string('c', 64),
            SourceRevision = SourceRevision,
            Language = "en",
            SelectedCategoryIds = [MealsCategoryId, DrinksCategoryId],
            Items = rows.ToList(),
            Categories =
            [
                Category(MealsCategoryId, "Meals", 0, 2),
                Category(DrinksCategoryId, "Drinks", 1, 1)
            ]
        };
        var sourceItems = rows.Select(row => new TenantCatalogueItem(row.ProductId, row.VariationId,
            row.ItemName, row.Description, row.VariationName, row.PriceMinor, row.Available, row.BlockReason)
        {
            SelectionKey = row.SelectionKey,
            CategoryId = row.CategoryId,
            CategoryName = row.CategoryName,
            CategoryDisplayOrder = row.CategoryDisplayOrder,
            ItemDisplayOrder = row.ItemDisplayOrder,
            SourceFingerprint = row.SourceFingerprint
        }).ToArray();
        var source = new TenantCatalogueSnapshot(SourceRevision, sourceItems)
        {
            Language = "en",
            Categories =
            [
                new(MealsCategoryId, "Meals", 0, 2, 2, 0, 2, 0, true),
                new(DrinksCategoryId, "Drinks", 1, 1, 1, 0, 1, 0, true)
            ]
        };
        return new(Template(), store, source);
    }

    private static TenantItemMapping Mapping(Guid productId, Guid? variationId, Guid categoryId,
        string categoryName, int categoryOrder, int itemOrder, string name, string? variationName,
        string description, int price, bool available)
        => new()
        {
            ProviderItemId = ItemId(productId, variationId),
            ProductId = productId,
            VariationId = variationId,
            VariationName = variationName,
            SelectionKey = $"{productId:D}:{variationId?.ToString("D") ?? "base"}",
            CategoryId = categoryId,
            CategoryName = categoryName,
            CategoryDisplayOrder = categoryOrder,
            ItemDisplayOrder = itemOrder,
            SourceFingerprint = new string('d', 64),
            ItemName = name,
            Description = description,
            PriceMinor = price,
            Available = available,
            Supported = true
        };

    private static TenantCategoryMapping Category(Guid id, string name, int order, int count)
        => new()
        {
            CategoryId = id,
            ProviderCategoryId = CategoryId(id),
            Name = name,
            DisplayOrder = order,
            Active = true,
            TotalItemCount = count,
            SupportedItemCount = count,
            SelectedItemCount = count
        };

    private static JsonElement ChangedTemplate(JsonElement source, string change)
    {
        var template = JsonNode.Parse(source.GetRawText())!.AsObject();
        var items = template["items"]!.AsArray();
        switch (change)
        {
            case "different-tax":
                items[1]!["tax_info"]!["vat_rate_percentage"] = 7.7m;
                break;
            case "invalid-tax":
                items[0]!["tax_info"]!["vat_rate_percentage"] = "unknown";
                break;
            case "price-metadata":
                items[1]!["price_info"]!["sale_price"] = 80;
                break;
            case "invalid-price":
                items[0]!["price_info"]!["price"] = "unknown";
                break;
            case "item-modifiers":
                items[0]!["modifier_group_ids"] = new JsonArray();
                break;
            case "global-modifiers":
                template["modifier_groups"] = new JsonArray(JsonNode.Parse("""{"id":"customization"}"""));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change));
        }
        return JsonSerializer.SerializeToElement(template);
    }

    private static JsonElement Template() => JsonDocument.Parse("""
        {
          "menus": [
            { "id": "menu-one", "category_ids": ["old-category"], "service_availability": [{ "day_of_week": "monday", "time_periods": [{ "start_time": "09:00", "end_time": "17:00" }] }] },
            { "id": "menu-two", "category_ids": ["old-category"], "service_availability": [{ "day_of_week": "tuesday", "time_periods": [{ "start_time": "10:00", "end_time": "18:00" }] }] }
          ],
          "categories": [
            { "id": "template-category-one", "title": { "translations": { "en_us": "Template" } }, "entities": [], "classification": "food" },
            { "id": "template-category-two", "title": { "translations": { "en_us": "Template" } }, "entities": [], "classification": "food" }
          ],
          "items": [
            { "id": "template-item-one", "title": { "translations": { "en_us": "Template" } }, "description": { "translations": { "en_us": "Template" } }, "price_info": { "price": 100, "sale_price": 100, "currency": "EUR" }, "tax_info": { "vat_rate_percentage": 8.1, "tax_category": "standard" }, "suspension_info": { "suspension": null, "overrides": [] } },
            { "id": "template-item-two", "title": { "translations": { "en_us": "Template" } }, "description": { "translations": { "en_us": "Template" } }, "price_info": { "price": 200, "sale_price": 100, "currency": "EUR" }, "tax_info": { "vat_rate_percentage": 8.1, "tax_category": "standard" }, "suspension_info": { "suspension": null, "overrides": [] } },
            { "id": "template-item-three", "title": { "translations": { "en_us": "Template" } }, "description": { "translations": { "en_us": "Template" } }, "price_info": { "price": 300, "sale_price": 100, "currency": "EUR" }, "tax_info": { "vat_rate_percentage": 8.1, "tax_category": "standard" }, "suspension_info": { "suspension": null, "overrides": [] } }
          ],
          "modifier_groups": []
        }
        """).RootElement.Clone();

    private static string ItemId(Guid productId, Guid? variationId)
        => $"sofra-item-{productId:N}{(variationId is { } id ? $"-{id:N}" : string.Empty)}";

    private static string CategoryId(Guid categoryId) => $"sofra-category-{categoryId:N}";

    private sealed record Fixture(JsonElement Template, TenantStoreBinding Store, TenantCatalogueSnapshot Source);
}
