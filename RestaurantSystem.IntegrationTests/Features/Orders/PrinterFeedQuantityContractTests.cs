using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderFromBasketCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class PrinterFeedQuantityContractTests : IntegrationTestBase
{
    private const string OrderNumber = "PF-GUEST-QTY-001";
    private const string GoldenFileName = "printer-feed-quantity-contract.golden.json";
    private const string BasketGoldenFileName = "printer-basket-order-quantity-contract.golden.json";
    private static readonly DateTime FixtureTime = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrderId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ComboProductId = Guid.Parse("10000000-0000-0000-0000-000000000011");
    private static readonly Guid TacoProductId = Guid.Parse("10000000-0000-0000-0000-000000000012");
    private static readonly Guid MeatProductId = Guid.Parse("10000000-0000-0000-0000-000000000013");
    private static readonly Guid SteakProductId = Guid.Parse("10000000-0000-0000-0000-000000000017");
    private static readonly Guid SideProduct1Id = Guid.Parse("10000000-0000-0000-0000-000000000014");
    private static readonly Guid SideProduct2Id = Guid.Parse("10000000-0000-0000-0000-000000000015");
    private static readonly Guid SideProduct3Id = Guid.Parse("10000000-0000-0000-0000-000000000016");
    private static readonly Guid RootOrderItemId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid TacoOrderItemId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid MeatOrderItemId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid SteakOrderItemId = Guid.Parse("20000000-0000-0000-0000-000000000007");
    private static readonly Guid SideOrderItem1Id = Guid.Parse("20000000-0000-0000-0000-000000000004");
    private static readonly Guid SideOrderItem2Id = Guid.Parse("20000000-0000-0000-0000-000000000005");
    private static readonly Guid SideOrderItem3Id = Guid.Parse("20000000-0000-0000-0000-000000000006");
    private static readonly Guid TacoMenuSectionItemId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid MeatMenuSectionItemId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid SteakMenuSectionItemId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid MainSectionId = Guid.Parse("31000000-0000-0000-0000-000000000001");
    private static readonly Guid MeatSectionId = Guid.Parse("31000000-0000-0000-0000-000000000002");
    private static readonly Guid SideAssociation1Id = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid SideAssociation2Id = Guid.Parse("40000000-0000-0000-0000-000000000002");
    private static readonly Guid SideAssociation3Id = Guid.Parse("40000000-0000-0000-0000-000000000003");
    private static readonly Guid IngredientId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid ExtraIngredientId = Guid.Parse("50000000-0000-0000-0000-000000000002");
    private static readonly Guid CorrectionJobId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid BasketCorrectionJobId = Guid.Parse("60000000-0000-0000-0000-000000000003");
    private static readonly Guid CorrectionAmendmentId = Guid.Parse("80000000-0000-0000-0000-000000000001");
    private static readonly Guid BasketCorrectionAmendmentId = Guid.Parse("80000000-0000-0000-0000-000000000002");
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public PrinterFeedQuantityContractTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task UnsupportedPrinterProjectionVersionIsAClientError(int projectionVersion)
    {
        AuthenticateAsDevice();
        var response = await Client.GetAsync($"/api/orders/printer-feed?projectionVersion={projectionVersion}");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HandBuiltOrderCannotFreezeClientSuppliedCompositionMetadata()
    {
        using var scope = Factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IOrderItemFactory>();
        var forgedRoot = new CreateOrderItemDto
        {
            ProductId = TacoProductId,
            Quantity = 1,
            IngredientQuantities = new Dictionary<Guid, int> { [ExtraIngredientId] = 2 },
            SuggestedSideItemId = SideAssociation1Id,
            ParentComponentMenuSectionItemId = TacoMenuSectionItemId,
            QuantityBasis = QuantityBasis.PerParentUnit,
            ConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
            CompositionRole = CompositionRole.Dish,
            PresentationLabel = "Forged root label",
            IngredientQuantityBasis = QuantityBasis.PerParentUnit,
            IngredientConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
            IngredientCompositionRoles = new Dictionary<Guid, CompositionRole>
            {
                [ExtraIngredientId] = CompositionRole.Extra,
            },
        };
        var order = new Order { CreatedAt = FixtureTime, CreatedBy = nameof(PrinterFeedQuantityContractTests) };

        var error = await factory.AddItemAsync(order, forgedRoot, itemsAreServerPriced: false,
            CancellationToken.None, allowStaffPrices: false);

        error.Should().BeNull();
        var root = order.Items.Single();
        root.QuantityBasis.Should().Be(QuantityBasis.Unknown);
        root.ConfigurationScope.Should().Be(ConfigurationScope.Unknown);
        root.CompositionRole.Should().Be(CompositionRole.Unknown);
        root.PresentationLabel.Should().BeNull();
        var ingredient = root.IngredientSnapshots.Single(row => row.IngredientId == ExtraIngredientId);
        ingredient.QuantityBasis.Should().Be(QuantityBasis.Unknown);
        ingredient.ConfigurationScope.Should().Be(ConfigurationScope.Unknown);
        ingredient.CompositionRole.Should().Be(CompositionRole.Ingredient);
        root.SuggestedSideItemId.Should().BeNull();
        root.ParentComponentOrderItemId.Should().BeNull();
    }

    [Fact]
    public async Task V1KeepsLineTotalCounts_AndV2CarriesFrozenPerParentCountsAndCorrectionSnapshots()
    {
        var v1 = await FetchFeedAsync(projectionVersion: null);
        var v2 = await FetchFeedAsync(projectionVersion: 2);
        var normalOrder = FindOrder(v1, OrderNumber);
        var projectedOrder = FindOrder(v2, OrderNumber);

        v1["data"]!.AsObject().Should().NotContainKey("projectionVersion");
        v2["data"]!["projectionVersion"]!.GetValue<int>().Should().Be(2);

        var normalTaco = FindChild(FindRoot(normalOrder, "Menu Deal"), "Taco");
        normalTaco["quantity"]!.GetValue<int>().Should().Be(2);
        normalTaco["quantityBasis"]!.GetValue<string>().Should().Be(nameof(QuantityBasis.LineTotal));
        var normalSides = normalTaco["sideItems"]!.AsArray();
        normalSides.Select(row => row!["quantity"]!.GetValue<int>()).Should().Equal(2, 4, 6);
        normalSides.Should().OnlyContain(row =>
            row!["quantityBasis"]!.GetValue<string>() == nameof(QuantityBasis.LineTotal));

        var taco = FindChild(FindRoot(projectedOrder, "Menu Deal"), "Taco");
        taco["quantity"]!.GetValue<int>().Should().Be(2);
        taco["quantityBasis"]!.GetValue<string>().Should().Be(nameof(QuantityBasis.LineTotal));
        var meat = FindChild(FindRoot(projectedOrder, "Menu Deal"), "Beef");
        meat["parentComponentOrderItemId"]!.GetValue<Guid>().Should().Be(TacoOrderItemId);
        meat["menuSectionItemId"]!.GetValue<Guid>().Should().Be(MeatMenuSectionItemId);
        meat["compositionRole"]!.GetValue<string>().Should().Be(nameof(CompositionRole.RequiredChoice));
        meat["presentationLabel"]!.GetValue<string>().Should().Be("Taco");
        meat["sectionId"]!.GetValue<Guid>().Should().Be(MeatSectionId);
        var steak = FindChild(FindRoot(projectedOrder, "Menu Deal"), "Steak");
        steak["quantity"]!.GetValue<int>().Should().Be(2);
        steak["sectionId"]!.GetValue<Guid>().Should().Be(MeatSectionId);
        steak["menuSectionItemId"]!.GetValue<Guid>().Should().Be(SteakMenuSectionItemId);
        steak["parentComponentOrderItemId"]!.GetValue<Guid>().Should().Be(TacoOrderItemId);
        steak["compositionRole"]!.GetValue<string>().Should().Be(nameof(CompositionRole.RequiredChoice));

        var sides = taco["sideItems"]!.AsArray();
        sides.Select(row => row!["quantity"]!.GetValue<int>()).Should().Equal(1, 2, 3);
        sides.Select(row => row!["quantityBasis"]!.GetValue<string>())
            .Should().OnlyContain(value => value == nameof(QuantityBasis.PerParentUnit));
        sides.Select(row => row!["configurationScope"]!.GetValue<string>())
            .Should().OnlyContain(value => value == nameof(ConfigurationScope.SharedAcrossParentUnits));
        sides.Select(row => row!["suggestedSideItemId"]!.GetValue<Guid>())
            .Should().Equal(SideAssociation1Id, SideAssociation2Id, SideAssociation3Id);
        sides.Should().OnlyContain(row =>
            row!["parentComponentOrderItemId"]!.GetValue<Guid>() == TacoOrderItemId);

        var ingredients = taco["ingredientCustomizations"]!.AsArray();
        ingredients.Should().HaveCount(2);
        ingredients.Select(row => row!["ingredientId"]!.GetValue<Guid>())
            .Should().OnlyContain(id => id == IngredientId);
        ingredients.Select(row => row!["presentationOrder"]!.GetValue<int>()).Should().Equal(1, 2);

        var update = v2["data"]!["updates"]!.AsArray()
            .Single(row => row!["jobId"]!.GetValue<Guid>() == CorrectionJobId)!;
        var change = update["changes"]!.AsArray().Single()!;
        AssertCorrectionSideQuantities(change["previous"]!);
        AssertCorrectionSideQuantities(change["current"]!);

        var actual = Normalize(v1, v2);
        if (UpdateMode)
        {
            await File.WriteAllTextAsync(SourceGoldenPath(), actual + Environment.NewLine);
            return;
        }

        var expected = await File.ReadAllTextAsync(OutputGoldenPath());
        actual.TrimEnd().Should().Be(expected.ReplaceLineEndings().TrimEnd(),
            "normal and v2 feed bytes changed; review both printer and browser consumers before updating the golden");
    }

    [Fact]
    public async Task BasketCheckoutFreezesChoiceSidesIngredientsAndV1V2Quantities()
    {
        var sessionId = Guid.NewGuid().ToString();
        Client.DefaultRequestHeaders.Add("X-Session-Id", sessionId);
        foreach (var quantity in new[] { 1, 2, 3 })
        {
            var addResponse = await Client.PostAsJsonAsync("/api/basket/items", BuildMenuLine(quantity));
            addResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
                await addResponse.Content.ReadAsStringAsync());
        }

        var createResponse = await Client.PostAsJsonAsync("/api/orders/from-basket", new CreateOrderFromBasketCommand
        {
            Type = OrderType.Takeaway,
            CustomerName = "Printer contract guest",
            Notes = "Basket to order quantity contract",
        });
        createResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK,
            await createResponse.Content.ReadAsStringAsync());
        var createResponseJson = JsonNode.Parse(await createResponse.Content.ReadAsStringAsync())!;
        var createResult = createResponseJson.Deserialize<ApiResponse<OrderDto>>(SerializerOptions);
        createResult!.Success.Should().BeTrue();
        var normalOrder = createResult.Data!;
        normalOrder.Items.Should().HaveCount(3);

        foreach (var quantity in new[] { 1, 2, 3 })
        {
            var root = normalOrder.Items.Single(item => item.SpecialInstructions == $"root-q{quantity}");
            root.Quantity.Should().Be(quantity);
            var dish = root.SideItems!.Single(item => item.ProductId == TacoProductId);
            dish.PresentationLabel.Should().Be("Taco");
            var beef = root.SideItems!.Single(item => item.ProductId == MeatProductId);
            var steakItem = root.SideItems!.Single(item => item.ProductId == SteakProductId);
            beef.Quantity.Should().Be(2 * quantity);
            steakItem.Quantity.Should().Be(quantity);
            beef.SectionId.Should().Be(MeatSectionId);
            steakItem.SectionId.Should().Be(MeatSectionId);
            beef.MenuSectionItemId.Should().Be(MeatMenuSectionItemId);
            steakItem.MenuSectionItemId.Should().Be(SteakMenuSectionItemId);
            beef.ParentComponentOrderItemId.Should().Be(dish.Id);
            steakItem.ParentComponentOrderItemId.Should().Be(dish.Id);
            beef.CompositionRole.Should().Be(CompositionRole.RequiredChoice);
            steakItem.CompositionRole.Should().Be(CompositionRole.RequiredChoice);
            beef.PresentationLabel.Should().Be("Taco");
            steakItem.PresentationLabel.Should().Be("Taco");
            beef.QuantityBasis.Should().Be(QuantityBasis.LineTotal);
            steakItem.QuantityBasis.Should().Be(QuantityBasis.LineTotal);

            var normalSides = dish.SideItems!;
            normalSides.Select(side => side.Quantity).Should().Equal(quantity, 2 * quantity, 3 * quantity);
            normalSides.Should().OnlyContain(side => side.QuantityBasis == QuantityBasis.LineTotal);
            normalSides.Select(side => side.SuggestedSideItemId)
                .Should().Equal(SideAssociation1Id, SideAssociation2Id, SideAssociation3Id);

            dish.IngredientCustomizations.Should().NotBeNull().And.HaveCount(2);
            dish.IngredientCustomizations!.Should().ContainSingle(row =>
                row.IngredientId == IngredientId && row.IsRemoved && row.Quantity == 0);
            dish.IngredientCustomizations.Should().ContainSingle(row =>
                row.IngredientId == ExtraIngredientId && row.IsAddOn && row.Quantity == 2);
            dish.IngredientCustomizations.Should().ContainSingle(row =>
                row.IngredientId == ExtraIngredientId && row.CompositionRole == CompositionRole.Extra);
            dish.SideItems!.Single(side => side.SuggestedSideItemId == SideAssociation3Id)
                .CompositionRole.Should().Be(CompositionRole.Drink);
        }

        var q2Root = normalOrder.Items.Single(item => item.SpecialInstructions == "root-q2");
        var q2Dish = q2Root.SideItems!.Single(item => item.ProductId == TacoProductId);
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
            var order = await context.Orders.Include(row => row.Items)
                .SingleAsync(row => row.Id == normalOrder.Id);
            order.Status = OrderStatus.Confirmed;
            order.IsKitchenReleased = true;
            context.OrderOperationalNotes.Add(new OrderOperationalNote
            {
                Id = BasketCorrectionJobId,
                OrderId = order.Id,
                Text = "Frozen correction snapshot from the created dish row",
                Audience = OrderNoteAudience.Kitchen,
                ClientOperationId = Guid.Parse("60000000-0000-0000-0000-000000000004"),
                AmendmentId = BasketCorrectionAmendmentId,
                KitchenTarget = DevicePrintTarget.General,
                KitchenChangesJson = JsonSerializer.Serialize(new[]
                {
                    new PrinterFeedChangeDto
                    {
                        Kind = KitchenChangeKind.Replace,
                        Previous = q2Dish,
                        Current = q2Dish,
                    }
                }),
                KitchenBoardSequence = 1,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = nameof(PrinterFeedQuantityContractTests),
            });
            await context.SaveChangesAsync();

        }

        var v1BeforeCatalogEdit = await FetchFeedAsync(projectionVersion: null);
        var v2BeforeCatalogEdit = await FetchFeedAsync(projectionVersion: 2);
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
            var menu = await context.Products.SingleAsync(product => product.Id == ComboProductId);
            var taco = await context.Products.Include(product => product.DetailedIngredients)
                .Include(product => product.SuggestedSideItems)
                .SingleAsync(product => product.Id == TacoProductId);
            var cheese = taco.DetailedIngredients.Single(row => row.Id == IngredientId);
            var sideProduct = await context.Products.SingleAsync(product => product.Id == SideProduct1Id);
            menu.Name = "Menu Deal catalog edit";
            taco.Name = "Taco catalog edit";
            cheese.Name = "Cheese catalog edit";
            sideProduct.Name = "Side One catalog edit";
            await context.SaveChangesAsync();
        }

        var v1 = await FetchFeedAsync(projectionVersion: null);
        var v2 = await FetchFeedAsync(projectionVersion: 2);
        var actualV1 = FindOrder(v1, normalOrder.OrderNumber);
        var actualV2 = FindOrder(v2, normalOrder.OrderNumber);
        var update = v2["data"]!["updates"]!.AsArray()
            .Single(row => row!["jobId"]!.GetValue<Guid>() == BasketCorrectionJobId)!;
        var actual = NormalizeBasketOrder(createResponseJson["data"]!, actualV1, v2, update);
        if (UpdateMode)
            await File.WriteAllTextAsync(SourceBasketGoldenPath(), actual + Environment.NewLine);
        var beforeV1 = FindOrder(v1BeforeCatalogEdit, normalOrder.OrderNumber);
        var beforeV2 = FindOrder(v2BeforeCatalogEdit, normalOrder.OrderNumber);
        JsonNode.DeepEquals(beforeV1["items"], actualV1["items"]).Should().BeTrue(
            "catalog edits must not change the normal historical order snapshot");
        JsonNode.DeepEquals(beforeV2["items"], actualV2["items"]).Should().BeTrue(
            "catalog edits must not change the versioned historical order snapshot");
        AssertCheckoutAndFeedParity(createResponseJson["data"]!, actualV1);
        var beforeRoot = normalOrder.Items.Single(item => item.SpecialInstructions == "root-q2");
        beforeRoot.ProductName.Should().Be("Menu Deal");
        beforeRoot.SideItems!.Single(item => item.ProductId == TacoProductId).IngredientCustomizations!
            .Select(row => row.IngredientName).Should().Equal("Cheese", "Chili");

        var v1Q2 = FindRootByInstructions(actualV1, "root-q2");
        var v2Q2 = FindRootByInstructions(actualV2, "root-q2");
        var v1Dish = FindChildByProductId(v1Q2, TacoProductId);
        var v2Dish = FindChildByProductId(v2Q2, TacoProductId);
        v1Q2["productName"]!.GetValue<string>().Should().Be("Menu Deal");
        v1Dish["productName"]!.GetValue<string>().Should().Be("Taco");
        v1Dish["presentationLabel"]!.GetValue<string>().Should().Be("Taco");
        v1Dish["ingredientCustomizations"]!.AsArray().Select(row => row!["ingredientName"]!.GetValue<string>())
            .Should().Equal("Cheese", "Chili");
        v1Dish["sideItems"]!.AsArray().Select(row => row!["productName"]!.GetValue<string>())
            .Should().Equal("Side One", "Side Two", "Side Three");
        v1Dish["sideItems"]!.AsArray().Single(row => row!["suggestedSideItemId"]!.GetValue<Guid>() == SideAssociation3Id)!["compositionRole"]!
            .GetValue<string>().Should().Be(nameof(CompositionRole.Drink));
        v2Dish["presentationLabel"]!.GetValue<string>().Should().Be("Taco");
        v2Dish["ingredientCustomizations"]!.AsArray().Single(row =>
            row!["ingredientId"]!.GetValue<Guid>() == ExtraIngredientId)!["compositionRole"]!
            .GetValue<string>().Should().Be(nameof(CompositionRole.Extra));
        v2Dish["sideItems"]!.AsArray().Single(row => row!["suggestedSideItemId"]!.GetValue<Guid>() == SideAssociation3Id)!["compositionRole"]!
            .GetValue<string>().Should().Be(nameof(CompositionRole.Drink));

        var v1Beef = FindChildByProductId(v1Q2, MeatProductId);
        var v1Steak = FindChildByProductId(v1Q2, SteakProductId);
        var v2Beef = FindChildByProductId(v2Q2, MeatProductId);
        var v2Steak = FindChildByProductId(v2Q2, SteakProductId);
        v1Beef["quantity"]!.GetValue<int>().Should().Be(4);
        v1Steak["quantity"]!.GetValue<int>().Should().Be(2);
        v2Beef["quantity"]!.GetValue<int>().Should().Be(4);
        v2Steak["quantity"]!.GetValue<int>().Should().Be(2);
        v2Beef["quantityBasis"]!.GetValue<string>().Should().Be(nameof(QuantityBasis.LineTotal));
        v2Steak["quantityBasis"]!.GetValue<string>().Should().Be(nameof(QuantityBasis.LineTotal));
        v1Dish["sideItems"]!.AsArray().Select(row => row!["quantity"]!.GetValue<int>())
            .Should().Equal(2, 4, 6);
        v2Dish["sideItems"]!.AsArray().Select(row => row!["quantity"]!.GetValue<int>())
            .Should().Equal(1, 2, 3);
        v2Dish["sideItems"]!.AsArray().Should().OnlyContain(row =>
            row!["quantityBasis"]!.GetValue<string>() == nameof(QuantityBasis.PerParentUnit)
            && row["configurationScope"]!.GetValue<string>() == nameof(ConfigurationScope.SharedAcrossParentUnits));

        var correction = update["changes"]!.AsArray().Single()!;
        correction["previous"]!["quantityBasis"]!.GetValue<string>().Should().Be(nameof(QuantityBasis.LineTotal));
        correction["previous"]!["sideItems"]!.AsArray().Select(row => row!["quantity"]!.GetValue<int>())
            .Should().Equal(2, 4, 6);
        correction["current"]!["sideItems"]!.AsArray().Select(row => row!["quantity"]!.GetValue<int>())
            .Should().Equal(2, 4, 6);

        if (UpdateMode)
        {
            return;
        }

        var expected = await File.ReadAllTextAsync(OutputBasketGoldenPath());
        actual.TrimEnd().Should().Be(expected.ReplaceLineEndings().TrimEnd(),
            "actual basket→CreateOrder→normal/V2 endpoint bytes changed; review browser and printer consumers before updating the golden");
    }

    [Fact]
    public async Task StandaloneDishVariationLabelFreezesThroughBasketCheckout()
    {
        var variationId = Guid.NewGuid();
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
            context.ProductVariations.Add(new ProductVariation
            {
                Id = variationId,
                ProductId = TacoProductId,
                Name = "Deluxe",
                PriceModifier = 1m,
                IsActive = true,
                DisplayOrder = 1,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = nameof(PrinterFeedQuantityContractTests),
            });
            var customerStepManifestJson = JsonSerializer.Serialize(new CustomerStepManifestDto
            {
                SchemaVersion = 1,
                Revision = 1,
                Steps =
                [
                    new CustomerStepManifestStepDto
                    {
                        Kind = CustomerStepKind.ProductVariation,
                        TargetId = variationId,
                        CompositionRole = CompositionRole.Dish,
                        PresentationLabel = "Taco Deluxe",
                        PresentationOrder = 1,
                    }
                ],
            }, SerializerOptions);
            await context.SaveChangesAsync();
            await context.Products.Where(row => row.Id == TacoProductId).ExecuteUpdateAsync(update => update
                .SetProperty(row => row.CustomerStepManifestRevision, 1)
                .SetProperty(row => row.CustomerStepManifestJson, customerStepManifestJson));
        }

        Client.DefaultRequestHeaders.Add("X-Session-Id", Guid.NewGuid().ToString());
        var add = await Client.PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = TacoProductId,
            ProductVariationId = variationId,
            Quantity = 1,
        });
        add.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await add.Content.ReadAsStringAsync());
        var checkout = await Client.PostAsJsonAsync("/api/orders/from-basket", new CreateOrderFromBasketCommand
        {
            Type = OrderType.Takeaway,
            CustomerName = "Standalone variation guest",
        });
        checkout.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await checkout.Content.ReadAsStringAsync());
        var result = (await checkout.Content.ReadFromJsonAsync<ApiResponse<OrderDto>>(SerializerOptions))!;
        result.Data!.Items.Should().ContainSingle();
        result.Data.Items[0].CompositionRole.Should().Be(CompositionRole.Dish);
        result.Data.Items[0].PresentationLabel.Should().Be("Taco Deluxe");
        result.Data.Items[0].PresentationOrder.Should().BeNull(
            "guest screen sequence is separate from frozen receipt ordering");
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext>();
        var combo = NewProduct(ComboProductId, "Menu Deal", ProductType.Menu);
        var tacoProduct = NewProduct(TacoProductId, "Taco", ProductType.MainItem);
        var beef = NewProduct(MeatProductId, "Beef", ProductType.AddOn);
        var steakProduct = NewProduct(SteakProductId, "Steak", ProductType.AddOn);
        var side1 = NewProduct(SideProduct1Id, "Side One", ProductType.AddOn);
        var side2 = NewProduct(SideProduct2Id, "Side Two", ProductType.AddOn);
        var side3 = NewProduct(SideProduct3Id, "Side Three", ProductType.Beverage);
        tacoProduct.DetailedIngredients.Add(new ProductIngredient
        {
            Id = IngredientId,
            ProductId = TacoProductId,
            Name = "Cheese",
            IsOptional = true,
            IsIncludedInBasePrice = true,
            IsActive = true,
            MaxQuantity = 1,
            DisplayOrder = 1,
            CreatedAt = FixtureTime,
            CreatedBy = nameof(PrinterFeedQuantityContractTests),
        });
        tacoProduct.DetailedIngredients.Add(new ProductIngredient
        {
            Id = ExtraIngredientId,
            ProductId = TacoProductId,
            Name = "Chili",
            IsOptional = true,
            IsIncludedInBasePrice = false,
            IsActive = true,
            MaxQuantity = 4,
            Price = 1.25m,
            DisplayOrder = 2,
            CreatedAt = FixtureTime,
            CreatedBy = nameof(PrinterFeedQuantityContractTests),
        });
        var definition = new MenuDefinition
        {
            Id = Guid.Parse("32000000-0000-0000-0000-000000000001"),
            ProductId = ComboProductId,
            Product = combo,
            IsAlwaysAvailable = true,
            CreatedAt = FixtureTime,
            CreatedBy = nameof(PrinterFeedQuantityContractTests),
        };
        var mainSection = NewSection(MainSectionId, definition.Id, "Main", 1, 1, 1, false);
        var meatSection = NewSection(MeatSectionId, definition.Id, "Viandes", 2, 3, 3, true);
        var tacoOption = NewSectionItem(TacoMenuSectionItemId, mainSection.Id, TacoProductId, 1, 0m);
        var beefOption = NewSectionItem(MeatMenuSectionItemId, meatSection.Id, MeatProductId, 1, 2m);
        var steakOption = NewSectionItem(SteakMenuSectionItemId, meatSection.Id, SteakProductId, 2, 1m);
        mainSection.Items.Add(tacoOption);
        meatSection.Items.Add(beefOption);
        meatSection.Items.Add(steakOption);
        definition.Sections.Add(mainSection);
        definition.Sections.Add(meatSection);
        combo.MenuDefinition = definition;
        tacoProduct.SuggestedSideItems.Add(new ProductSideItem
        {
            Id = SideAssociation1Id,
            MainProductId = TacoProductId,
            SideItemProductId = SideProduct1Id,
            DisplayOrder = 1,
            CreatedAt = FixtureTime,
            CreatedBy = nameof(PrinterFeedQuantityContractTests),
        });
        tacoProduct.SuggestedSideItems.Add(new ProductSideItem
        {
            Id = SideAssociation2Id,
            MainProductId = TacoProductId,
            SideItemProductId = SideProduct2Id,
            DisplayOrder = 2,
            CreatedAt = FixtureTime,
            CreatedBy = nameof(PrinterFeedQuantityContractTests),
        });
        tacoProduct.SuggestedSideItems.Add(new ProductSideItem
        {
            Id = SideAssociation3Id,
            MainProductId = TacoProductId,
            SideItemProductId = SideProduct3Id,
            DisplayOrder = 3,
            CreatedAt = FixtureTime,
            CreatedBy = nameof(PrinterFeedQuantityContractTests),
        });
        combo.CustomerStepManifestRevision = 1;
        combo.CustomerStepManifestJson = CreateManifestJson();
        context.Products.AddRange(combo, tacoProduct, beef, steakProduct, side1, side2, side3);
        context.MenuDefinitions.Add(definition);

        var order = new Order
        {
            Id = OrderId,
            OrderNumber = OrderNumber,
            Type = OrderType.Takeaway,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 60m,
            Total = 60m,
            OrderDate = FixtureTime,
            CreatedAt = FixtureTime,
            CreatedBy = nameof(PrinterFeedQuantityContractTests),
            IsKitchenReleased = true,
        };
        var root = NewItem(RootOrderItemId, ComboProductId, "Menu Deal", 2, 30m, 60m, null,
            CompositionRole.Menu, QuantityBasis.LineTotal, ConfigurationScope.SharedAcrossParentUnits, null, null, 0);
        var taco = NewItem(TacoOrderItemId, TacoProductId, "Taco", 2, 4m, 0m, root.Id,
            CompositionRole.Dish, QuantityBasis.LineTotal, ConfigurationScope.SharedAcrossParentUnits,
            TacoMenuSectionItemId, null, 1, sectionId: MainSectionId, kind: OrderItemKind.BundleChild);
        var meat = NewItem(MeatOrderItemId, MeatProductId, "Beef", 4, 0m, 0m, root.Id,
            CompositionRole.RequiredChoice, QuantityBasis.LineTotal, ConfigurationScope.SharedAcrossParentUnits,
            MeatMenuSectionItemId, taco.Id, 2, sectionId: MeatSectionId, kind: OrderItemKind.BundleChild);
        var steakOrderItem = NewItem(SteakOrderItemId, SteakProductId, "Steak", 2, 0m, 0m, root.Id,
            CompositionRole.RequiredChoice, QuantityBasis.LineTotal, ConfigurationScope.SharedAcrossParentUnits,
            SteakMenuSectionItemId, taco.Id, 3, sectionId: MeatSectionId, kind: OrderItemKind.BundleChild);
        var sides = new[]
        {
            NewItem(SideOrderItem1Id, SideProduct1Id, "Side One", 1, 2m, 0m, taco.Id,
                CompositionRole.Side, QuantityBasis.PerParentUnit, ConfigurationScope.SharedAcrossParentUnits,
                null, taco.Id, 3, SideAssociation1Id, OrderItemKind.SideItem),
            NewItem(SideOrderItem2Id, SideProduct2Id, "Side Two", 2, 3m, 0m, taco.Id,
                CompositionRole.Side, QuantityBasis.PerParentUnit, ConfigurationScope.SharedAcrossParentUnits,
                null, taco.Id, 4, SideAssociation2Id, OrderItemKind.SideItem),
            NewItem(SideOrderItem3Id, SideProduct3Id, "Side Three", 3, 4m, 0m, taco.Id,
                CompositionRole.Drink, QuantityBasis.PerParentUnit, ConfigurationScope.SharedAcrossParentUnits,
                null, taco.Id, 5, SideAssociation3Id, OrderItemKind.SideItem),
        };
        taco.IngredientSnapshots.Add(NewIngredientSnapshot(
            Guid.Parse("70000000-0000-0000-0000-000000000001"), taco.Id, 2, 1));
        taco.IngredientSnapshots.Add(NewIngredientSnapshot(
            Guid.Parse("70000000-0000-0000-0000-000000000002"), taco.Id, 1, 2));
        order.Items.Add(root);
        order.Items.Add(taco);
        order.Items.Add(meat);
        order.Items.Add(steakOrderItem);
        foreach (var side in sides) order.Items.Add(side);
        context.Orders.Add(order);
        context.OrderOperationalNotes.Add(new OrderOperationalNote
        {
            Id = CorrectionJobId,
            OrderId = order.Id,
            Text = "Correct selected sides",
            Audience = OrderNoteAudience.Kitchen,
            ClientOperationId = Guid.Parse("60000000-0000-0000-0000-000000000002"),
            AmendmentId = CorrectionAmendmentId,
            KitchenTarget = DevicePrintTarget.General,
            KitchenChangesJson = JsonSerializer.Serialize(new[]
            {
                new PrinterFeedChangeDto
                {
                    Kind = KitchenChangeKind.Replace,
                    Previous = BuildCorrectionTaco(),
                    Current = BuildCorrectionTaco()
                }
            }),
            KitchenBoardSequence = 1,
            CreatedAt = FixtureTime,
            CreatedBy = nameof(PrinterFeedQuantityContractTests),
        });
        await context.SaveChangesAsync();
    }

    private async Task<JsonNode> FetchFeedAsync(int? projectionVersion)
    {
        AuthenticateAsDevice();
        var path = "/api/orders/printer-feed";
        if (projectionVersion.HasValue) path += $"?projectionVersion={projectionVersion.Value}";
        var response = await Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["success"]!.GetValue<bool>().Should().BeTrue(body["message"]?.ToString());
        return body;
    }

    private static JsonNode FindOrder(JsonNode feed, string orderNumber) =>
        feed["data"]!["items"]!.AsArray()
            .Single(row => row!["orderNumber"]!.GetValue<string>() == orderNumber)!;

    private static JsonNode FindRoot(JsonNode order, string productName) => order["items"]!.AsArray()
        .Single(row => row!["productName"]!.GetValue<string>() == productName)!;

    private static JsonNode FindChild(JsonNode parent, string productName) => parent["sideItems"]!.AsArray()
        .Single(row => row!["productName"]!.GetValue<string>() == productName)!;

    private static JsonNode FindRootByInstructions(JsonNode order, string instructions) => order["items"]!.AsArray()
        .Single(row => row!["specialInstructions"]!.GetValue<string>() == instructions)!;

    private static JsonNode FindChildByProductId(JsonNode parent, Guid productId) => parent["sideItems"]!.AsArray()
        .Single(row => row!["productId"]!.GetValue<Guid>() == productId)!;

    private static void AssertCheckoutAndFeedParity(JsonNode checkoutOrder, JsonNode feedOrder)
    {
        var checkoutRoots = checkoutOrder["items"]!.AsArray();
        var feedRoots = feedOrder["items"]!.AsArray();
        feedRoots.Should().HaveCount(checkoutRoots.Count);
        foreach (var checkoutRoot in checkoutRoots)
        {
            var instructions = checkoutRoot!["specialInstructions"]!.GetValue<string>();
            var feedRoot = FindRootByInstructions(feedOrder, instructions);
            AssertSameOrderFields(checkoutRoot, feedRoot);

            var checkoutChildren = checkoutRoot["sideItems"]!.AsArray();
            var feedChildren = feedRoot["sideItems"]!.AsArray();
            feedChildren.Should().HaveCount(checkoutChildren.Count);
            foreach (var checkoutChild in checkoutChildren)
            {
                var productId = checkoutChild!["productId"]!.GetValue<Guid>();
                var feedChild = FindChildByProductId(feedRoot, productId);
                AssertSameOrderFields(checkoutChild, feedChild);
                AssertSameMetadataFields(checkoutChild, feedChild);

                var checkoutSides = checkoutChild["sideItems"]?.AsArray() ?? [];
                var feedSides = feedChild["sideItems"]?.AsArray() ?? [];
                feedSides.Should().HaveCount(checkoutSides.Count);
                foreach (var checkoutSide in checkoutSides)
                {
                    var associationId = checkoutSide!["suggestedSideItemId"]!.GetValue<Guid>();
                    var feedSide = feedSides.Single(row => row!["suggestedSideItemId"]!.GetValue<Guid>() == associationId)!;
                    AssertSameOrderFields(checkoutSide, feedSide);
                    AssertSameMetadataFields(checkoutSide, feedSide);
                }

                var checkoutIngredients = checkoutChild["ingredientCustomizations"]?.AsArray() ?? [];
                var feedIngredients = feedChild["ingredientCustomizations"]?.AsArray() ?? [];
                feedIngredients.Should().HaveCount(checkoutIngredients.Count);
                foreach (var checkoutIngredient in checkoutIngredients)
                {
                    var order = checkoutIngredient!["presentationOrder"]!.GetValue<int>();
                    var feedIngredient = feedIngredients.Single(row => row!["presentationOrder"]!.GetValue<int>() == order)!;
                    AssertSameMetadataFields(checkoutIngredient, feedIngredient);
                    checkoutIngredient["ingredientId"]!.GetValue<Guid>()
                        .Should().Be(feedIngredient["ingredientId"]!.GetValue<Guid>());
                    checkoutIngredient["quantity"]!.GetValue<int>()
                        .Should().Be(feedIngredient["quantity"]!.GetValue<int>());
                }
            }
        }
    }

    private static void AssertSameOrderFields(JsonNode expected, JsonNode actual)
    {
        foreach (var field in new[] { "id", "productId", "productName", "specialInstructions" })
            expected[field]?.ToString().Should().Be(actual[field]?.ToString(), $"{field} remains frozen");
        expected["quantity"]?.GetValue<int>().Should().Be(actual["quantity"]?.GetValue<int>());
        foreach (var field in new[] { "unitPrice", "itemTotal" })
        {
            var expectedValue = expected[field];
            var actualValue = actual[field];
            if (expectedValue is null || actualValue is null)
                actualValue.Should().BeNull($"{field} remains frozen");
            else
                expectedValue.GetValue<decimal>().Should().Be(actualValue.GetValue<decimal>(), $"{field} remains frozen");
        }
    }

    private static void AssertSameMetadataFields(JsonNode expected, JsonNode actual)
    {
        foreach (var field in new[]
        {
            "sectionId", "menuSectionItemId", "parentComponentOrderItemId", "suggestedSideItemId",
            "compositionRole", "quantityBasis", "configurationScope", "presentationLabel", "presentationOrder",
        })
            expected[field]?.ToString().Should().Be(actual[field]?.ToString(), $"{field} remains frozen");
    }

    private static AddToBasketDto BuildMenuLine(int quantity) => new()
    {
        ProductId = ComboProductId,
        Quantity = quantity,
        SpecialInstructions = $"root-q{quantity}",
        SelectedMenuOptions =
        [
            new SelectedMenuOptionDto
            {
                SectionId = MainSectionId,
                ItemId = TacoProductId,
                Quantity = 1,
                SelectedIngredients = [ExtraIngredientId],
                IngredientQuantities = new Dictionary<Guid, int> { [ExtraIngredientId] = 2 },
                SelectedSideItems =
                [
                    new SelectedSideItemDto { Id = SideProduct1Id, SuggestedSideItemId = SideAssociation1Id, Quantity = 1 },
                    new SelectedSideItemDto { Id = SideProduct2Id, SuggestedSideItemId = SideAssociation2Id, Quantity = 2 },
                    new SelectedSideItemDto { Id = SideProduct3Id, SuggestedSideItemId = SideAssociation3Id, Quantity = 3 },
                ],
            },
            new SelectedMenuOptionDto
            {
                SectionId = MeatSectionId,
                MenuSectionItemId = MeatMenuSectionItemId,
                ItemId = MeatProductId,
                Quantity = 2,
            },
            new SelectedMenuOptionDto
            {
                SectionId = MeatSectionId,
                MenuSectionItemId = SteakMenuSectionItemId,
                ItemId = SteakProductId,
                Quantity = 1,
            },
        ],
    };

    private static void AssertCorrectionSideQuantities(JsonNode item)
    {
        item["compositionRole"]!.GetValue<string>().Should().Be(nameof(CompositionRole.Dish));
        var sides = item["sideItems"]!.AsArray();
        sides.Select(row => row!["quantity"]!.GetValue<int>()).Should().Equal(1, 2, 3);
        sides.Should().OnlyContain(row =>
            row!["quantityBasis"]!.GetValue<string>() == nameof(QuantityBasis.PerParentUnit)
            && row["configurationScope"]!.GetValue<string>() == nameof(ConfigurationScope.SharedAcrossParentUnits));
    }

    private static string Normalize(JsonNode v1, JsonNode v2)
    {
        var v1Order = JsonNode.Parse(FindOrder(v1, OrderNumber).ToJsonString())!;
        var v2Order = JsonNode.Parse(FindOrder(v2, OrderNumber).ToJsonString())!;
        var v2Update = v2["data"]!["updates"]!.AsArray()
            .Single(row => row!["jobId"]!.GetValue<Guid>() == CorrectionJobId)!.DeepClone();
        return new JsonObject
        {
            ["normalOrder"] = v1Order,
            ["projectionVersion"] = v2["data"]!["projectionVersion"]!.DeepClone(),
            ["v2Order"] = v2Order,
            ["v2Update"] = v2Update,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings();
    }

    private static string NormalizeBasketOrder(JsonNode normalOrder, JsonNode v1Order, JsonNode v2Feed, JsonNode update)
    {
        var document = new JsonObject
        {
            ["normalOrder"] = normalOrder.DeepClone(),
            ["v1PrinterOrder"] = v1Order.DeepClone(),
            ["projectionVersion"] = v2Feed["data"]!["projectionVersion"]!.DeepClone(),
            ["v2PrinterOrder"] = FindOrder(v2Feed, normalOrder["orderNumber"]!.GetValue<string>()).DeepClone(),
            ["v2Update"] = update.DeepClone(),
        };
        NormalizeDynamicValues(document);
        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings();
    }

    private static void NormalizeDynamicValues(JsonNode node)
    {
        var canonicalIds = new Dictionary<Guid, Guid>();
        var nextId = 1;
        Visit(node, null);

        void Visit(JsonNode? current, string? propertyName)
        {
            if (current is JsonObject obj)
            {
                foreach (var pair in obj.ToList()) Visit(pair.Value, pair.Key);
                return;
            }
            if (current is JsonArray array)
            {
                foreach (var child in array) Visit(child, propertyName);
                return;
            }
            if (current is not JsonValue value || !value.TryGetValue<string>(out var raw)) return;

            if (string.Equals(propertyName, "orderNumber", StringComparison.OrdinalIgnoreCase))
            {
                Replace(current, "ORDER-NORMALIZED");
                return;
            }
            if (string.Equals(propertyName, "guestStatusToken", StringComparison.OrdinalIgnoreCase))
            {
                Replace(current, "guest-status-token-normalized");
                return;
            }
            if (propertyName is not null && (propertyName.EndsWith("At", StringComparison.OrdinalIgnoreCase)
                || string.Equals(propertyName, "orderDate", StringComparison.OrdinalIgnoreCase)))
            {
                Replace(current, "2026-10-10T12:00:00Z");
                return;
            }
            if (!Guid.TryParse(raw, out var id)) return;

            var stableId = StableCatalogIds.Contains(id);
            if (stableId) return;
            if (!canonicalIds.TryGetValue(id, out var canonicalId))
            {
                canonicalId = Guid.Parse($"00000000-0000-0000-0000-{nextId++:D12}");
                canonicalIds.Add(id, canonicalId);
            }
            Replace(current, canonicalId.ToString());
        }

        static void Replace(JsonNode original, string text)
        {
            if (original.Parent is JsonObject parent)
            {
                var key = parent.First(pair => ReferenceEquals(pair.Value, original)).Key;
                parent[key] = text;
            }
            else if (original.Parent is JsonArray array)
            {
                var index = array.IndexOf(original);
                array[index] = text;
            }
        }
    }

    private static readonly HashSet<Guid> StableCatalogIds =
    [
        ComboProductId, TacoProductId, MeatProductId, SteakProductId,
        SideProduct1Id, SideProduct2Id, SideProduct3Id,
        TacoMenuSectionItemId, MeatMenuSectionItemId, SteakMenuSectionItemId,
        MainSectionId, MeatSectionId,
        SideAssociation1Id, SideAssociation2Id, SideAssociation3Id,
        IngredientId, ExtraIngredientId,
    ];

    private static Product NewProduct(Guid id, string name, ProductType type) => new()
    {
        Id = id,
        Name = name,
        BasePrice = 10m,
        IsActive = true,
        IsAvailable = true,
        Type = type,
        KitchenType = KitchenType.FrontKitchen,
        CreatedAt = FixtureTime,
        CreatedBy = nameof(PrinterFeedQuantityContractTests),
    };

    private static MenuSection NewSection(
        Guid id, Guid menuDefinitionId, string name, int order, int minimum, int maximum, bool repeated) => new()
        {
            Id = id,
            MenuDefinitionId = menuDefinitionId,
            Name = name,
            DisplayOrder = order,
            IsRequired = true,
            MinSelection = minimum,
            MaxSelection = maximum,
            AllowRepeatedItems = repeated,
            CreatedAt = FixtureTime,
            CreatedBy = nameof(PrinterFeedQuantityContractTests),
        };

    private static MenuSectionItem NewSectionItem(
        Guid id, Guid sectionId, Guid productId, int order, decimal additionalPrice) => new()
        {
            Id = id,
            MenuSectionId = sectionId,
            ProductId = productId,
            DisplayOrder = order,
            AdditionalPrice = additionalPrice,
            CreatedAt = FixtureTime,
            CreatedBy = nameof(PrinterFeedQuantityContractTests),
        };

    private static string CreateManifestJson()
    {
        var manifest = new CustomerStepManifestDto
        {
            SchemaVersion = 1,
            Revision = 1,
            Steps =
            [
                new CustomerStepManifestStepDto
                {
                    Kind = CustomerStepKind.BundleSection,
                    TargetId = MainSectionId,
                    CompositionRole = CompositionRole.Dish,
                    PresentationLabel = "Taco",
                    PresentationOrder = 1,
                },
                new CustomerStepManifestStepDto
                {
                    Kind = CustomerStepKind.BundleSection,
                    TargetId = MeatSectionId,
                    ParentComponentId = TacoMenuSectionItemId,
                    CompositionRole = CompositionRole.RequiredChoice,
                    PresentationOrder = 2,
                },
                new CustomerStepManifestStepDto
                {
                    Kind = CustomerStepKind.BundleComponentIngredient,
                    SectionId = MainSectionId,
                    SectionItemId = TacoMenuSectionItemId,
                    ProductId = TacoProductId,
                    ScopeId = IngredientId,
                    CompositionRole = CompositionRole.Ingredient,
                    PresentationOrder = 3,
                },
                new CustomerStepManifestStepDto
                {
                    Kind = CustomerStepKind.BundleComponentIngredient,
                    SectionId = MainSectionId,
                    SectionItemId = TacoMenuSectionItemId,
                    ProductId = TacoProductId,
                    ScopeId = ExtraIngredientId,
                    CompositionRole = CompositionRole.Extra,
                    PresentationOrder = 6,
                },
                new CustomerStepManifestStepDto
                {
                    Kind = CustomerStepKind.BundleComponentSide,
                    SectionId = MainSectionId,
                    SectionItemId = TacoMenuSectionItemId,
                    ProductId = TacoProductId,
                    ScopeId = SideAssociation1Id,
                    CompositionRole = CompositionRole.Side,
                    PresentationOrder = 4,
                },
                new CustomerStepManifestStepDto
                {
                    Kind = CustomerStepKind.BundleComponentSide,
                    SectionId = MainSectionId,
                    SectionItemId = TacoMenuSectionItemId,
                    ProductId = TacoProductId,
                    ScopeId = SideAssociation2Id,
                    CompositionRole = CompositionRole.Side,
                    PresentationOrder = 4,
                },
                new CustomerStepManifestStepDto
                {
                    Kind = CustomerStepKind.BundleComponentSide,
                    SectionId = MainSectionId,
                    SectionItemId = TacoMenuSectionItemId,
                    ProductId = TacoProductId,
                    ScopeId = SideAssociation3Id,
                    CompositionRole = CompositionRole.Drink,
                    PresentationOrder = 5,
                },
            ]
        };
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        });
    }

    private static OrderItem NewItem(
        Guid id, Guid productId, string name, int quantity, decimal unitPrice, decimal itemTotal,
        Guid? parentId, CompositionRole role, QuantityBasis basis, ConfigurationScope scope,
        Guid? menuSectionItemId, Guid? parentComponentId, int order,
        Guid? suggestedSideItemId = null, OrderItemKind? kind = null, Guid? sectionId = null) => new()
        {
            Id = id,
            OrderId = OrderId,
            ProductId = productId,
            ProductName = name,
            Quantity = quantity,
            UnitPrice = unitPrice,
            ItemTotal = itemTotal,
            ParentOrderItemId = parentId,
            SectionId = sectionId,
            ParentComponentOrderItemId = parentComponentId,
            MenuSectionItemId = menuSectionItemId,
            SuggestedSideItemId = suggestedSideItemId,
            QuantityBasis = basis,
            ConfigurationScope = scope,
            CompositionRole = role,
            PresentationLabel = parentComponentId.HasValue ? "Taco" : null,
            PresentationOrder = order,
            Kind = kind,
            CreatedAt = FixtureTime.AddMinutes(order),
            CreatedBy = nameof(PrinterFeedQuantityContractTests),
        };

    private static OrderItemIngredient NewIngredientSnapshot(Guid id, Guid itemId, int quantity, int order) => new()
    {
        Id = id,
        OrderItemId = itemId,
        IngredientId = IngredientId,
        IngredientName = "Extra Cheese",
        Quantity = quantity,
        QuantityBasis = QuantityBasis.PerParentUnit,
        ConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
        CompositionRole = CompositionRole.Ingredient,
        PresentationOrder = order,
        IsAddOn = true,
        IsRemoved = false,
        SortOrder = order,
        CreatedAt = FixtureTime,
        CreatedBy = nameof(PrinterFeedQuantityContractTests),
    };

    private static OrderItemDto BuildCorrectionTaco() => new()
    {
        Id = TacoOrderItemId,
        ProductId = TacoProductId,
        ProductName = "Taco",
        Quantity = 2,
        UnitPrice = 4m,
        ItemTotal = 0m,
        QuantityBasis = QuantityBasis.LineTotal,
        ConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
        CompositionRole = CompositionRole.Dish,
        MenuSectionItemId = TacoMenuSectionItemId,
        SideItems = new List<OrderItemDto>
        {
            CorrectionSide(SideOrderItem1Id, SideProduct1Id, "Side One", SideAssociation1Id, 1, 3),
            CorrectionSide(SideOrderItem2Id, SideProduct2Id, "Side Two", SideAssociation2Id, 2, 4),
            CorrectionSide(SideOrderItem3Id, SideProduct3Id, "Side Three", SideAssociation3Id, 3, 5),
        },
        IngredientCustomizations = new List<OrderItemIngredientDto>
        {
            new()
            {
                IngredientId = IngredientId, IngredientName = "Extra Cheese", Quantity = 2,
                IsAddOn = true, QuantityBasis = QuantityBasis.PerParentUnit,
                ConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
                CompositionRole = CompositionRole.Ingredient, PresentationOrder = 1
            },
            new()
            {
                IngredientId = IngredientId, IngredientName = "Extra Cheese", Quantity = 1,
                IsAddOn = true, QuantityBasis = QuantityBasis.PerParentUnit,
                ConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
                CompositionRole = CompositionRole.Ingredient, PresentationOrder = 2
            },
        }
    };

    private static OrderItemDto CorrectionSide(
        Guid id, Guid productId, string name, Guid associationId, int quantity, int order) => new()
        {
            Id = id,
            ProductId = productId,
            ProductName = name,
            Quantity = quantity,
            UnitPrice = 2m,
            ItemTotal = 0m,
            Kind = OrderItemKind.SideItem,
            QuantityBasis = QuantityBasis.PerParentUnit,
            ConfigurationScope = ConfigurationScope.SharedAcrossParentUnits,
            CompositionRole = CompositionRole.Side,
            ParentComponentOrderItemId = TacoOrderItemId,
            SuggestedSideItemId = associationId,
            PresentationLabel = "Taco",
            PresentationOrder = order,
        };

    private static bool UpdateMode => Environment.GetEnvironmentVariable("UPDATE_CONTRACT_SNAPSHOTS") == "1";

    private static string SourceGoldenPath([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, GoldenFileName);

    private static string OutputGoldenPath() =>
        Path.Combine(AppContext.BaseDirectory, "Features", "Orders", GoldenFileName);

    private static string SourceBasketGoldenPath([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, BasketGoldenFileName);

    private static string OutputBasketGoldenPath() =>
        Path.Combine(AppContext.BaseDirectory, "Features", "Orders", BasketGoldenFileName);
}
