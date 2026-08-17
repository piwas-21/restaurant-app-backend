using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// Issue #313: the login merge matches an anonymous line to a user line on identity alone —
// ParentBasketItemId, ProductId, ProductVariationId — with no customization comparison. The ADD path
// runs the same identity query and then filters it with IsSameCustomization, deliberately, so that
// two differently-customised lines of the same product stay separate (#155).
//
// Two notions of "the same line" in one codebase is the defect; the visible damage is that a guest's
// paid extras vanish at login, or that extras nobody asked for appear.
//
// WHY THE EXISTING MERGE SUITE IS BLIND: AnonymousBasketMergeIntegrationTest adds every line through
// `new AddToBasketDto { ProductId, Quantity }` — identical and uncustomised on both sides — which is
// exactly the case where matching on identity alone gives the right answer.
//
// Customization here is a SIDE ITEM rather than an ingredient, following BasketLineTotalTests: it is
// priced unconditionally by BuildRegularItemAsync, so the fixture does not depend on the ingredient
// rules and their optional / included-in-base branches.
[Collection("Database Lane 4")]
public class LoginMergeCustomizationTests : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private readonly Guid _userId = Guid.Parse(TestAuthHandler.UserId);
    private Product _pizza = null!;
    private Product _cola = null!;

    public LoginMergeCustomizationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _pizza = await context.Products.FirstAsync(p => p.Name == "Test Pizza");
        _cola = await context.Products.FirstAsync(p => p.Name == "Test Cola");
    }

    private async Task AddPlainAsync(string? sessionId, Guid? userId, int quantity)
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(sessionId!, userId,
            new AddToBasketDto { ProductId = _pizza.Id, Quantity = quantity });
    }

    private async Task AddCustomisedAsync(string? sessionId, Guid? userId, int quantity)
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(sessionId!, userId, new AddToBasketDto
        {
            ProductId = _pizza.Id,
            Quantity = quantity,
            SelectedSideItems = new List<SelectedSideItemDto>
            {
                new() { Id = _cola.Id, Quantity = 1 }
            }
        });
    }

    private async Task MergeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.MergeAnonymousBasketAsync(_sessionId, _userId);
    }

    /// <summary>
    /// The surviving user basket's ROOT rows, read from the database in a fresh scope. Read from
    /// stored state rather than the returned DTO: the merge's damage is a hard DELETE plus a quantity
    /// mutation, and only the rows can show that a line is gone rather than merely unmapped.
    /// </summary>
    private async Task<List<BasketItem>> ReadUserRootsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var basket = await context.Baskets
            .Include(b => b.Items)
            .SingleAsync(b => b.UserId == _userId && !b.IsDeleted);

        return basket.Items.Where(i => i.ParentBasketItemId == null).ToList();
    }

    private decimal PlainLineTotal(int quantity) => _pizza.BasePrice * quantity;
    private decimal CustomisedLineTotal(int quantity) => (_pizza.BasePrice + _cola.BasePrice) * quantity;

    // The guest's extras must not be deleted by logging in. Anonymous line is customised, the user's
    // is plain: matching on identity alone keeps the USER's row, adds the anonymous quantity to it,
    // and hard-deletes the anonymous row — the side item is gone, unpaid for and unprinted.
    [Fact]
    public async Task CustomisedAnonymousLine_DoesNotMergeIntoAPlainUserLine()
    {
        await AddCustomisedAsync(_sessionId, null, 1);
        await AddPlainAsync(null, _userId, 1);

        await MergeAsync();

        var roots = await ReadUserRootsAsync();

        roots.Should().HaveCount(2, "a customised line and a plain line are not the same line — the add path has always treated them as distinct");

        var customised = roots.Should().ContainSingle(r => r.CustomizationPrice != 0m).Subject;
        customised.Quantity.Should().Be(1);
        customised.CustomizationPrice.Should().Be(_cola.BasePrice);
        customised.SelectedSideItemsJson.Should().NotBeNullOrEmpty("the guest paid for this side item");
        customised.ItemTotal.Should().Be(CustomisedLineTotal(1));

        var plain = roots.Should().ContainSingle(r => r.CustomizationPrice == 0m).Subject;
        plain.Quantity.Should().Be(1);
        plain.ItemTotal.Should().Be(PlainLineTotal(1));
    }

    // The mirror, which is the worse half: the guest's PLAIN unit is absorbed into the user's
    // customised line, so they are charged for a side item they never chose and the kitchen makes it.
    [Fact]
    public async Task PlainAnonymousLine_DoesNotMergeIntoACustomisedUserLine()
    {
        await AddPlainAsync(_sessionId, null, 1);
        await AddCustomisedAsync(null, _userId, 1);

        await MergeAsync();

        var roots = await ReadUserRootsAsync();

        roots.Should().HaveCount(2, "a plain line must not inherit the extras of a customised one");

        var customised = roots.Should().ContainSingle(r => r.CustomizationPrice != 0m).Subject;
        customised.Quantity.Should().Be(1, "the anonymous plain unit must NOT be added to the customised line");
        customised.ItemTotal.Should().Be(CustomisedLineTotal(1));

        var plain = roots.Should().ContainSingle(r => r.CustomizationPrice == 0m).Subject;
        plain.Quantity.Should().Be(1);
        plain.SelectedSideItemsJson.Should().BeNullOrEmpty("nothing was chosen on this line");
        plain.ItemTotal.Should().Be(PlainLineTotal(1));
    }

    // Identical customization on both sides MUST still merge. This is the case the merge exists for,
    // and it is the one a fix could easily break by refusing every match — which would leave the guest
    // with two rows where they expect one, and is why it is pinned alongside the two above.
    [Fact]
    public async Task IdenticallyCustomisedLines_StillMerge()
    {
        await AddCustomisedAsync(_sessionId, null, 1);
        await AddCustomisedAsync(null, _userId, 2);

        await MergeAsync();

        var roots = await ReadUserRootsAsync();

        var line = roots.Should().ContainSingle("same product, same customization — one line").Subject;
        line.Quantity.Should().Be(3);
        line.CustomizationPrice.Should().Be(_cola.BasePrice);
        line.ItemTotal.Should().Be(CustomisedLineTotal(3),
            "the merged total goes through BasketLineTotal.ForRoot, so the side item is charged per unit (#308)");
    }

    // Plain-into-plain: the behaviour the existing merge suite already covers, pinned here so a fix
    // that over-refuses is caught on the simplest possible input.
    [Fact]
    public async Task IdenticalPlainLines_StillMerge()
    {
        await AddPlainAsync(_sessionId, null, 1);
        await AddPlainAsync(null, _userId, 2);

        await MergeAsync();

        var roots = await ReadUserRootsAsync();

        var line = roots.Should().ContainSingle().Subject;
        line.Quantity.Should().Be(3);
        line.ItemTotal.Should().Be(PlainLineTotal(3));
    }
}
