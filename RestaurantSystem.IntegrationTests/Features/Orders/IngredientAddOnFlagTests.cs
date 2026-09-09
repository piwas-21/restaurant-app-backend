using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Commands.CreateOrderCommand;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// The <c>isAddOn</c> flag on <see cref="OrderItemIngredientDto"/>: true when a row is a PAID
/// EXTRA the guest opted into (an optional ingredient not included in the base price). It exists
/// because a freshly-chosen add-on carries quantity 1 — wire-identical to a base-recipe default —
/// so every display surface that showed only removals and quantity &gt; 1 hid the guest's ordinary
/// "extra sauce" choice (admin order details, cashier, the printed kitchen ticket).
/// <para>
/// Both read paths are pinned, because they source the flag differently:
///  - a placed order renders its FROZEN snapshot rows (S1), which carry the flag as a column —
///    a later optionality edit or recipe deletion must not reclass a rendered line;
///  - a legacy line with no snapshot (pre-S1, never backfilled) renders through the
///    <c>ProjectRecipe</c> projection, which computes the flag from the live recipe row.
/// </para>
/// </summary>
[Collection("Database Lane 2")]
public class IngredientAddOnFlagTests : IntegrationTestBase
{
    public IngredientAddOnFlagTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const string LegacyOrderNumber = "ADDON-FLAG-LEGACY";

    private static readonly Guid CheeseId = Guid.NewGuid();     // optional, included in base price
    private static readonly Guid MushroomsId = Guid.NewGuid();  // optional PAID EXTRA
    private static readonly Guid OlivesId = Guid.NewGuid();     // optional PAID EXTRA (stays unchosen)
    private static readonly Guid TomatoSauceId = Guid.NewGuid(); // required

    private Product _pizza = null!;

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _pizza = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Addon Flag Pizza",
            BasePrice = 12.00m,
            Type = ProductType.MainItem,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            IsActive = true,
            IsAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            DetailedIngredients =
            [
                new ProductIngredient
                {
                    Id = CheeseId, ProductId = Guid.Empty, Name = "Cheese",
                    IsOptional = true, IsIncludedInBasePrice = true, Price = 1.00m,
                    MaxQuantity = 2, IsActive = true, DisplayOrder = 1,
                    CreatedAt = DateTime.UtcNow, CreatedBy = "test"
                },
                new ProductIngredient
                {
                    Id = MushroomsId, ProductId = Guid.Empty, Name = "Mushrooms",
                    IsOptional = true, IsIncludedInBasePrice = false, Price = 2.00m,
                    MaxQuantity = 3, IsActive = true, DisplayOrder = 2,
                    CreatedAt = DateTime.UtcNow, CreatedBy = "test"
                },
                new ProductIngredient
                {
                    Id = OlivesId, ProductId = Guid.Empty, Name = "Olives",
                    IsOptional = true, IsIncludedInBasePrice = false, Price = 1.50m,
                    MaxQuantity = 1, IsActive = true, DisplayOrder = 3,
                    CreatedAt = DateTime.UtcNow, CreatedBy = "test"
                },
                new ProductIngredient
                {
                    Id = TomatoSauceId, ProductId = Guid.Empty, Name = "Tomato Sauce",
                    IsOptional = false, IsIncludedInBasePrice = false, Price = 0m,
                    MaxQuantity = 1, IsActive = true, DisplayOrder = 4,
                    CreatedAt = DateTime.UtcNow, CreatedBy = "test"
                }
            ]
        };
        foreach (var ingredient in _pizza.DetailedIngredients)
        {
            ingredient.ProductId = _pizza.Id;
        }

        // A pre-snapshot (legacy) line: an id map and NO OrderItemIngredient rows. The guest chose
        // mushrooms (paid extra, quantity 1) and explicitly zeroed olives; cheese was never
        // mentioned. This is every order placed before S1 — there is no backfill.
        var legacyOrder = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = LegacyOrderNumber,
            Type = OrderType.DineIn,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        legacyOrder.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = legacyOrder.Id,
            ProductId = _pizza.Id,
            ProductName = _pizza.Name,
            Quantity = 1,
            UnitPrice = 12.00m,
            ItemTotal = 14.00m,
            IngredientQuantitiesJson = JsonSerializer.Serialize(
                new Dictionary<Guid, int> { [MushroomsId] = 1, [OlivesId] = 0 }),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        context.AddRange(_pizza, legacyOrder);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateOrder_ChosenPaidExtraAtDefaultQuantity_IsMarkedAddOn()
    {
        AuthenticateAsAdmin();

        // The case that motivated the flag: mushrooms (a paid extra) chosen at quantity 1 — the
        // quantity every fresh add-on carries. Olives explicitly zeroed (unchosen). Cheese left at
        // its in-base default.
        var request = new CreateOrderCommand
        {
            Type = OrderType.DineIn,
            TableNumber = 3,
            CustomerName = "Addon Flag Customer",
            Items =
            [
                new CreateOrderItemDto
                {
                    ProductId = _pizza.Id,
                    Quantity = 1,
                    UnitPrice = 14.00m,
                    IngredientQuantities = new Dictionary<Guid, int>
                    {
                        [CheeseId] = 1,
                        [MushroomsId] = 1,
                        [OlivesId] = 0,
                        [TomatoSauceId] = 1
                    }
                }
            ],
            Payments = [new CreateOrderPaymentDto { PaymentMethod = PaymentMethod.Cash, Amount = 20.00m }]
        };

        var response = await PostAsJsonAsync("/api/orders", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Data!;

        var item = created.Items.Single(i => i.ProductId == _pizza.Id);
        item.IngredientCustomizations.Should().NotBeNull();

        var mushrooms = item.IngredientCustomizations!.Single(c => c.IngredientId == MushroomsId);
        mushrooms.IsAddOn.Should().BeTrue("a paid extra the guest opted into, even at quantity 1");
        mushrooms.Quantity.Should().Be(1);
        mushrooms.IsRemoved.Should().BeFalse();

        item.IngredientCustomizations!.Single(c => c.IngredientId == CheeseId).IsAddOn
            .Should().BeFalse("cheese is included in the base price — a base-recipe row");
        item.IngredientCustomizations!.Single(c => c.IngredientId == TomatoSauceId).IsAddOn
            .Should().BeFalse("a required ingredient is always part of the base recipe");

        var olives = item.IngredientCustomizations!.Single(c => c.IngredientId == OlivesId);
        olives.IsAddOn.Should().BeTrue("the flag says what the row IS");
        olives.Quantity.Should().Be(0);
        olives.IsRemoved.Should().BeFalse("a paid extra nobody picked was never there to remove");

        // The same order read back through GET /api/orders/{id} — the FROZEN snapshot path, where
        // name AND flag come off the OrderItemIngredient column. This is what the admin
        // order-details modal and the printer feed consume.
        var readResponse = await Client.GetAsync($"/api/orders/{created.Id}");
        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reread = (await ReadResponseAsync<ApiResponse<OrderDto>>(readResponse))!.Data!;

        var rereadItem = reread.Items.Single(i => i.ProductId == _pizza.Id);
        rereadItem.IngredientCustomizations.Should().NotBeNull("the snapshot rows must load on read");
        var rereadMushrooms = rereadItem.IngredientCustomizations!.Single(c => c.IngredientId == MushroomsId);
        rereadMushrooms.IsAddOn.Should().BeTrue();
        rereadMushrooms.IngredientName.Should().Be("Mushrooms");
        rereadItem.IngredientCustomizations!.Single(c => c.IngredientId == CheeseId).IsAddOn.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrderById_LegacyLineWithoutSnapshot_DerivesAddOnFromLiveRecipe()
    {
        AuthenticateAsAdmin();

        Guid legacyOrderId;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            legacyOrderId = await context.Orders
                .Where(o => o.OrderNumber == LegacyOrderNumber)
                .Select(o => o.Id)
                .SingleAsync();
        }

        var response = await Client.GetAsync($"/api/orders/{legacyOrderId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!.Data!;

        var item = dto.Items.Single();
        item.IngredientCustomizations.Should().NotBeNull("the legacy fallback projection must run");

        var mushrooms = item.IngredientCustomizations!.Single(c => c.IngredientId == MushroomsId);
        mushrooms.Quantity.Should().Be(1);
        mushrooms.IsRemoved.Should().BeFalse();
        mushrooms.IsAddOn.Should().BeTrue("the live recipe marks mushrooms a paid extra");

        var olives = item.IngredientCustomizations!.Single(c => c.IngredientId == OlivesId);
        olives.Quantity.Should().Be(0);
        olives.IsRemoved.Should().BeFalse();
        olives.IsAddOn.Should().BeTrue();

        // Cheese is optional-but-in-base and absent from the saved map, so it renders no row at
        // all — the pre-existing fallback behavior, unchanged by the flag.
        item.IngredientCustomizations!.Should().NotContain(c => c.IngredientId == CheeseId);
    }

}
