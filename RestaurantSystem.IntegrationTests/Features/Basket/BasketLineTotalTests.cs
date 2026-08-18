using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// Issue #308: a root line's ItemTotal is recomputed in three places, and the three did not agree.
//
// The two shapes a root row can have are priced by DIFFERENT rules, because the factory builds them
// differently:
//
//   regular item  BuildRegularItemAsync   UnitPrice EXCLUDES customization
//                                         ItemTotal = (UnitPrice + CustomizationPrice) * Quantity
//   bundle parent BuildMenuItemAsync      UnitPrice INCLUDES customization (it is folded in at the
//                                         end, and CustomizationPrice keeps a copy for display)
//                                         ItemTotal = UnitPrice * Quantity
//
// Every recompute site has to pick one, and each of the three had hard-coded a single formula:
//
//   AnonymousBasketMerger   (UnitPrice + CustomizationPrice) * Quantity  -> DOUBLE-charged a bundle
//   BasketService add-dedup  Quantity * UnitPrice                        -> DROPPED a regular item's
//   BasketService update     Quantity * UnitPrice                           customization
//
// The bundle half is what #308 reports and is asserted in BundleChildQuantityRescaleTests, next to
// the merge it happens on. The regular half is the inverse error at the other two sites and is
// asserted here: both are undercharges, and both fire on the most ordinary cart actions there are —
// adding the same customised dish twice, and moving its quantity stepper.
//
// Customization here comes from a SIDE ITEM rather than an ingredient, deliberately: it is priced
// unconditionally (BuildRegularItemAsync adds `sideItem.BasePrice * quantity`), so the fixture does
// not depend on the ingredient rules, which have their own optional / included-in-base branches and
// their own issues. IsSameCustomization compares side items, so the add-path dedup still matches.
[Collection("Database Lane 1")]
public class BasketLineTotalTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private Product _testPizza = null!;
    private Product _testCola = null!;

    public BasketLineTotalTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _testPizza = await context.Products.FirstAsync(p => p.Name == "Test Pizza");
        _testCola = await context.Products.FirstAsync(p => p.Name == "Test Cola");
    }

    // Pizza 12.99 with one cola (2.99) as a side item: UnitPrice 12.99, CustomizationPrice 2.99.
    private Task<HttpResponseMessage> AddCustomisedPizzaAsync(int quantity) =>
        PostAsJsonAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = _testPizza.Id,
            Quantity = quantity,
            SelectedSideItems = new List<SelectedSideItemDto>
            {
                new() { Id = _testCola.Id, Quantity = 1 }
            }
        });

    private decimal UnitPlusCustomization => _testPizza.BasePrice + _testCola.BasePrice;

    /// <summary>
    /// The stored row and the basket's stored SubTotal. Read from the DATABASE rather than the
    /// response DTO: the mapping layer copies ItemTotal straight through, so a DTO-only assertion
    /// could not tell a corrected row from a corrected projection, and it is the stored value that
    /// BasketPricingService sums and that the order pipeline later reads.
    /// </summary>
    private async Task<(BasketItem Line, decimal SubTotal)> ReadPizzaLineAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var basket = await context.Baskets
            .Include(b => b.Items)
            .SingleAsync(b => b.SessionId == _sessionId && !b.IsDeleted);

        return (basket.Items.Single(i => i.ParentBasketItemId == null && i.ProductId == _testPizza.Id),
                basket.SubTotal);
    }

    // Site 1: AddItemToBasketAsync's exact-match dedup. Ordering the same customised dish a second
    // time is the single most ordinary way to reach this line, and `Quantity * UnitPrice` silently
    // drops the side item from the charge — the guest is shown, and billed, a cheaper cart than the
    // one they built.
    [Fact]
    public async Task AddingTheSameCustomisedLineTwice_KeepsTheCustomizationInTheLineTotal()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddCustomisedPizzaAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);

        var (built, builtSubTotal) = await ReadPizzaLineAsync();
        built.UnitPrice.Should().Be(_testPizza.BasePrice, "the factory keeps customization OUT of a regular item's UnitPrice");
        built.CustomizationPrice.Should().Be(_testCola.BasePrice);
        built.ItemTotal.Should().Be(UnitPlusCustomization, "the add-time total is the contract the merge has to preserve");
        builtSubTotal.Should().Be(UnitPlusCustomization);

        (await AddCustomisedPizzaAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);

        var (merged, mergedSubTotal) = await ReadPizzaLineAsync();
        merged.Quantity.Should().Be(2, "the identical customization dedups into the existing line");
        merged.CustomizationPrice.Should().Be(_testCola.BasePrice, "the merge must not disturb the stored customization price");
        merged.ItemTotal.Should().Be(UnitPlusCustomization * 2,
            "the second add must charge the side item too — `Quantity * UnitPrice` billed 25.98 for two 15.98 lines");
        mergedSubTotal.Should().Be(UnitPlusCustomization * 2);
    }

    // Site 2: UpdateBasketItemAsync, i.e. the cart's quantity stepper. Worse than site 1 because it
    // does not need a quantity CHANGE to bite: the row is rewritten from UnitPrice alone, so even
    // re-submitting the quantity it already holds drops the customization.
    [Fact]
    public async Task ChangingTheQuantityOfACustomisedLine_KeepsTheCustomizationInTheLineTotal()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddCustomisedPizzaAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (built, _) = await ReadPizzaLineAsync();

        var response = await PutAsJsonAsync($"/api/basket/items/{built.Id}",
            new UpdateBasketItemDto { Quantity = 3 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var (updated, subTotal) = await ReadPizzaLineAsync();
        updated.Quantity.Should().Be(3);
        updated.ItemTotal.Should().Be(UnitPlusCustomization * 3,
            "the stepper must not drop the side item — `Quantity * UnitPrice` billed 38.97 instead of 47.94");
        subTotal.Should().Be(UnitPlusCustomization * 3);
    }

    // The same stepper, quantity unchanged. Pinned separately because it is the case that makes the
    // defect reachable without the guest doing anything a cart would call an edit, and because a fix
    // that keyed off "the quantity moved" would pass the test above and still lose money here.
    [Fact]
    public async Task ReSubmittingTheSameQuantity_DoesNotDropTheCustomization()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddCustomisedPizzaAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (built, _) = await ReadPizzaLineAsync();

        var response = await PutAsJsonAsync($"/api/basket/items/{built.Id}",
            new UpdateBasketItemDto { Quantity = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var (updated, subTotal) = await ReadPizzaLineAsync();
        updated.Quantity.Should().Be(1);
        updated.ItemTotal.Should().Be(UnitPlusCustomization);
        subTotal.Should().Be(UnitPlusCustomization);
    }

    // The other retype direction (MainItem -> Menu), and the reason the rule keys on the row's own
    // child count rather than on Product.Type. `product.Type = command.Type` in UpdateProductCommand
    // is unguarded, so an admin can retype a product while a customised line of it sits in someone's
    // basket. This row was built by BuildRegularItemAsync and its UnitPrice therefore excludes the
    // side item, whatever the product now claims to be — a rule that trusted the type would drop the
    // 2.99 and bill 25.98 instead of 31.96.
    [Fact]
    public async Task ChangingTheQuantityAfterTheProductIsRetypedToMenu_StillPricesItAsARegularItem()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        (await AddCustomisedPizzaAsync(1)).StatusCode.Should().Be(HttpStatusCode.OK);
        var (built, _) = await ReadPizzaLineAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await context.Products.SingleAsync(p => p.Id == _testPizza.Id);
            product.Type = ProductType.Menu;
            await context.SaveChangesAsync();
        }

        var response = await PutAsJsonAsync($"/api/basket/items/{built.Id}",
            new UpdateBasketItemDto { Quantity = 2 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var (updated, subTotal) = await ReadPizzaLineAsync();
        updated.Quantity.Should().Be(2);
        updated.ItemTotal.Should().Be(UnitPlusCustomization * 2,
            "the row has no children, so it is priced as what built it, not as what the product now is");
        subTotal.Should().Be(UnitPlusCustomization * 2);
    }

    // A line with NO customization must keep behaving exactly as before. CustomizationPrice is 0
    // there, so both candidate formulas agree — which is precisely why this case cannot be used to
    // detect the defect, and why it is worth pinning that the fix does not disturb it.
    [Fact]
    public async Task AnUncustomisedLine_IsUnaffected()
    {
        Client.DefaultRequestHeaders.Add("X-Session-Id", _sessionId);

        var plain = new AddToBasketDto { ProductId = _testPizza.Id, Quantity = 1 };
        (await PostAsJsonAsync("/api/basket/items", plain)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsJsonAsync("/api/basket/items", plain)).StatusCode.Should().Be(HttpStatusCode.OK);

        var (merged, subTotal) = await ReadPizzaLineAsync();
        merged.Quantity.Should().Be(2);
        merged.CustomizationPrice.Should().Be(0m);
        merged.ItemTotal.Should().Be(_testPizza.BasePrice * 2);
        subTotal.Should().Be(_testPizza.BasePrice * 2);
    }
}
