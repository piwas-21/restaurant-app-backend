using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Services;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

public class ExplicitCustomizationSelectionTests
{
    [Fact]
    public void RequiredGroupBelowMinimum_IsRejected()
    {
        var (product, group, _, _) = Fixture();

        var act = () => ExplicitCustomizationSelection.Resolve(product,
            [new() { GroupId = group.Id, Options = [] }]);

        act.Should().Throw<BadRequestException>().WithMessage("*requires 1 to 2*");
    }

    [Fact]
    public void GroupAboveMaximum_IsRejected()
    {
        var (product, group, ingredientOption, productOption) = Fixture(max: 1);
        var request = Selection(group.Id,
            (CustomizationOptionKind.Ingredient, ingredientOption.Id, 1),
            (CustomizationOptionKind.Product, productOption.Id, 1));

        var act = () => ExplicitCustomizationSelection.Resolve(product, [request]);

        act.Should().Throw<BadRequestException>().WithMessage("*requires 1 to 1*");
    }

    [Fact]
    public void ForeignMembership_IsRejected()
    {
        var (product, group, _, _) = Fixture();
        var request = Selection(group.Id, (CustomizationOptionKind.Ingredient, Guid.NewGuid(), 1));

        var act = () => ExplicitCustomizationSelection.Resolve(product, [request]);

        act.Should().Throw<BadRequestException>().WithMessage("*does not belong*");
    }

    [Fact]
    public void BothKindsResolveToServerOwnedTargets()
    {
        var (product, group, ingredientOption, productOption) = Fixture();
        var request = Selection(group.Id,
            (CustomizationOptionKind.Ingredient, ingredientOption.Id, 2),
            (CustomizationOptionKind.Product, productOption.Id, 1));

        var result = ExplicitCustomizationSelection.Resolve(product, [request]);

        result.SelectedIngredientIds.Should().Equal(ingredientOption.ProductIngredientId);
        result.IngredientQuantities.Should().Contain(ingredientOption.ProductIngredientId, 2);
        result.ProductOptions.Should().ContainSingle()
            .Which.MembershipId.Should().Be(productOption.Id);
    }

    [Fact]
    public void LegacyProductKeepsLegacyPayload()
    {
        var product = Product("Legacy");

        var result = ExplicitCustomizationSelection.Resolve(product, null);

        result.SelectedIngredientIds.Should().BeEmpty();
        result.ProductOptions.Should().BeEmpty();
    }

    [Fact]
    public void PersistedSelection_RevalidatesIngredientAndProductMemberships()
    {
        var (product, _, ingredientOption, productOption) = Fixture();
        var row = new BasketItem
        {
            Quantity = 1,
            SelectedIngredients = [ingredientOption.ProductIngredientId],
            CreatedBy = "test"
        };
        var child = new BasketItem
        {
            Quantity = 1,
            ProductCustomizationOptionId = productOption.Id,
            CreatedBy = "test"
        };

        var act = () => ExplicitCustomizationSelection.EnsurePersisted(product, row, [child]);

        act.Should().NotThrow();
    }

    [Fact]
    public void ProductOptionChild_ReachesOrderAsCustomizationOption()
    {
        var membershipId = Guid.NewGuid();
        var translated = new BasketToOrderTranslator().Translate(
        [
            new BasketItemDto
            {
                ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 13m, ItemTotal = 13m,
                ChildItems =
                [
                    new()
                    {
                        ProductId = Guid.NewGuid(), ProductCustomizationOptionId = membershipId,
                        Quantity = 1, UnitPrice = 3m
                    }
                ]
            }
        ]);

        translated.Single().ChildItems!.Single().Kind.Should().Be(OrderItemKind.CustomizationOption);
    }

    private static CustomizationGroupSelectionDto Selection(
        Guid groupId, params (CustomizationOptionKind Kind, Guid Id, int Quantity)[] options) => new()
        {
            GroupId = groupId,
            Options = options.Select(option => new CustomizationOptionSelectionDto
            {
                Kind = option.Kind,
                OptionId = option.Id,
                Quantity = option.Quantity
            }).ToList()
        };

    private static (Product Product, ProductCustomizationGroup Group,
        ProductCustomizationIngredientOption Ingredient, ProductCustomizationProductOption ProductOption) Fixture(int max = 2)
    {
        var product = Product("Tacos");
        var ingredient = new ProductIngredient
        {
            Id = Guid.NewGuid(),
            Product = product,
            ProductId = product.Id,
            Name = "Sauce",
            IsActive = true,
            IsOptional = true,
            MaxQuantity = 2,
            CreatedBy = "test"
        };
        product.DetailedIngredients.Add(ingredient);
        var component = Product("Chicken");
        var group = new ProductCustomizationGroup
        {
            Id = Guid.NewGuid(),
            Product = product,
            ProductId = product.Id,
            Name = "Choices",
            IsActive = true,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = max,
            CreatedBy = "test"
        };
        var ingredientOption = new ProductCustomizationIngredientOption
        {
            Id = Guid.NewGuid(),
            ProductCustomizationGroup = group,
            ProductCustomizationGroupId = group.Id,
            ProductIngredient = ingredient,
            ProductIngredientId = ingredient.Id,
            CreatedBy = "test"
        };
        var productOption = new ProductCustomizationProductOption
        {
            Id = Guid.NewGuid(),
            ProductCustomizationGroup = group,
            ProductCustomizationGroupId = group.Id,
            OptionProduct = component,
            OptionProductId = component.Id,
            AdditionalPrice = 3m,
            CreatedBy = "test"
        };
        group.IngredientOptions.Add(ingredientOption);
        group.ProductOptions.Add(productOption);
        product.CustomizationGroups.Add(group);
        return (product, group, ingredientOption, productOption);
    }

    private static Product Product(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        BasePrice = 10m,
        IsActive = true,
        IsAvailable = true,
        CreatedBy = "test"
    };
}
