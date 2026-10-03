using FluentAssertions;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

public sealed class BasketPurchaseFingerprintTests
{
    private static readonly Guid RootA = Guid.Parse("d9260000-0000-0000-0000-000000000001");
    private static readonly Guid RootB = Guid.Parse("d9260000-0000-0000-0000-000000000002");
    private static readonly Guid ChildA = Guid.Parse("d9260000-0000-0000-0000-000000000003");
    private static readonly Guid ChildB = Guid.Parse("d9260000-0000-0000-0000-000000000004");
    private static readonly Guid IngredientA = Guid.Parse("d9260000-0000-0000-0000-000000000005");
    private static readonly Guid IngredientB = Guid.Parse("d9260000-0000-0000-0000-000000000006");
    private readonly IBasketToOrderTranslator _translator = new BasketToOrderTranslator();

    [Fact]
    public void Fingerprint_ignores_display_text_and_basket_line_order()
    {
        var first = Fingerprint(OrderType.DineIn, [RootLine(RootA), RootLine(RootB)]);
        var reversedWithDifferentDisplay = Fingerprint(OrderType.DineIn,
        [
            RootLine(RootB, localized: true, reverseChildren: true),
            RootLine(RootA, localized: true, reverseChildren: true),
        ]);

        first.Should().HaveLength(64);
        BasketPurchaseFingerprint.IsValidDigest(first).Should().BeTrue();
        reversedWithDifferentDisplay.Should().Be(first);
        Fingerprint(OrderType.Takeaway, [RootLine(RootA), RootLine(RootB)]).Should().NotBe(first);
    }

    [Theory]
    [InlineData("note ", 2, 3.25, 2)]
    [InlineData("note", 3, 3.25, 2)]
    [InlineData("note", 2, 3.50, 2)]
    [InlineData("note", 2, 3.25, 4)]
    public void Fingerprint_changes_for_nested_preparation_quantity_price_or_ingredient_changes(
        string instruction, int quantity, decimal unitPrice, int ingredientQuantity)
    {
        var baseline = Fingerprint(OrderType.DineIn,
            [RootLine(RootA, nestedInstruction: "note", nestedQuantity: 2, nestedUnitPrice: 3.25m, ingredientQuantity: 2)]);
        var changed = Fingerprint(OrderType.DineIn,
            [RootLine(RootA, nestedInstruction: instruction, nestedQuantity: quantity,
                nestedUnitPrice: unitPrice, ingredientQuantity: ingredientQuantity)]);

        changed.Should().NotBe(baseline);
    }

    private string Fingerprint(OrderType? type, IReadOnlyList<BasketItemDto> lines) =>
        BasketPurchaseFingerprint.Compute(type, lines, _translator);

    private static BasketItemDto RootLine(
        Guid productId,
        bool localized = false,
        bool reverseChildren = false,
        string nestedInstruction = "note",
        int nestedQuantity = 2,
        decimal nestedUnitPrice = 3.25m,
        int ingredientQuantity = 2)
    {
        var childA = new BasketItemDto
        {
            ProductId = ChildA,
            ProductCustomizationOptionId = ChildA,
            ProductName = localized ? "Nom traduit" : "Original child",
            Quantity = nestedQuantity,
            UnitPrice = nestedUnitPrice,
            SpecialInstructions = nestedInstruction,
            IngredientQuantities = new Dictionary<Guid, int> { [IngredientA] = ingredientQuantity, [IngredientB] = 0 },
            SelectedIngredients = [IngredientA],
        };
        var childB = new BasketItemDto
        {
            ProductId = ChildB,
            ProductName = localized ? "Composant" : "Component",
            Quantity = 1,
            UnitPrice = 1.50m,
            IngredientQuantities = new Dictionary<Guid, int> { [IngredientA] = 0 },
            SelectedIngredients = [],
        };
        var children = reverseChildren ? new List<BasketItemDto> { childB, childA } : [childA, childB];

        return new BasketItemDto
        {
            ProductId = productId,
            ProductName = localized ? "Nom traduit" : "Original",
            ProductDescription = localized ? "Description locale" : "Description",
            ProductImageUrl = localized ? "/locale/image" : "/image",
            ProductVariationId = Guid.Parse("d9260000-0000-0000-0000-000000000007"),
            ProductCustomizationOptionId = Guid.Parse("d9260000-0000-0000-0000-000000000008"),
            VariationName = localized ? "Grand" : "Large",
            VariationContent = new Dictionary<string, BasketItemVariationContentDto>
            {
                [localized ? "fr" : "en"] = new(localized ? "Grand" : "Large", null),
            },
            Quantity = 1,
            UnitPrice = 10m,
            ItemTotal = 10m,
            SpecialInstructions = "root wording",
            SelectedIngredients = [IngredientA],
            AddedIngredients = [IngredientB],
            IngredientQuantities = new Dictionary<Guid, int> { [IngredientA] = 1, [IngredientB] = 0 },
            SelectedIngredientNames = [localized ? "Tomates" : "Tomatoes"],
            AddedIngredientNames = [localized ? "Fromage" : "Cheese"],
            RemovedIngredientNames = [localized ? "Oignons" : "Onions"],
            SelectedSideItems =
            [
                new BasketSideItemDto
                {
                    Id = Guid.Parse("d9260000-0000-0000-0000-000000000009"),
                    Name = localized ? "Accompagnement" : "Side",
                    Price = 2m,
                    Quantity = 1,
                },
            ],
            ChildItems = children,
        };
    }
}
