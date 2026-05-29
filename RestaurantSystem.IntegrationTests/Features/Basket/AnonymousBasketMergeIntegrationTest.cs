using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

// Locks in the PR #89 review fix: the anonymous→user basket merge must PERSIST the basket-header
// mutations (adopt UserId / soft-delete). The original AsNoTracking load silently dropped them.
// Each operation runs in its own DI scope (separate DbContext) so assertions read freshly from the
// database — proving persistence, not in-memory tracking.
public class AnonymousBasketMergeIntegrationTest : IntegrationTestBase
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private readonly Guid _userId = Guid.Parse(TestAuthHandler.UserId);
    private Guid _pizzaId;
    private Guid _colaId;

    public AnonymousBasketMergeIntegrationTest(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _pizzaId = (await context.Products.FirstAsync(p => p.Name == "Test Pizza")).Id;
        _colaId = (await context.Products.FirstAsync(p => p.Name == "Test Cola")).Id;
    }

    private async Task AddItemAsync(string? sessionId, Guid? userId, Guid productId, int quantity)
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(sessionId!, userId,
            new AddToBasketDto { ProductId = productId, Quantity = quantity });
    }

    private async Task MergeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.MergeAnonymousBasketAsync(_sessionId, _userId);
    }

    [Fact]
    public async Task Merge_WhenUserHasNoBasket_AdoptsAnonymousBasketAndPersists()
    {
        await AddItemAsync(_sessionId, null, _pizzaId, 2);

        await MergeAsync();

        using var verifyScope = Factory.Services.CreateScope();
        var context = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The basket is now owned by the user — the adopt actually persisted.
        var userBaskets = await context.Baskets.Include(b => b.Items)
            .Where(b => b.UserId == _userId)
            .ToListAsync();
        userBaskets.Should().HaveCount(1);
        userBaskets[0].Items.Should().ContainSingle(i => i.ProductId == _pizzaId && i.Quantity == 2);

        // No lingering anonymous (session, no-user) basket remains.
        var anon = await context.Baskets
            .FirstOrDefaultAsync(b => b.SessionId == _sessionId && b.UserId == null);
        anon.Should().BeNull();
    }

    [Fact]
    public async Task Merge_WhenUserHasBasket_SumsDuplicates_MovesDistinct_AndSoftDeletesAnonymous()
    {
        // User basket: pizza x1. Anonymous basket: pizza x2 (duplicate) + cola x1 (distinct).
        await AddItemAsync(null, _userId, _pizzaId, 1);
        await AddItemAsync(_sessionId, null, _pizzaId, 2);
        await AddItemAsync(_sessionId, null, _colaId, 1);

        Guid anonBasketId;
        using (var pre = Factory.Services.CreateScope())
        {
            var ctx = pre.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            anonBasketId = (await ctx.Baskets
                .FirstAsync(b => b.SessionId == _sessionId && b.UserId == null)).Id;
        }

        await MergeAsync();

        using var verifyScope = Factory.Services.CreateScope();
        var context = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var userBasket = await context.Baskets.Include(b => b.Items)
            .FirstAsync(b => b.UserId == _userId);
        userBasket.Items.Should().Contain(i => i.ProductId == _pizzaId && i.Quantity == 3); // 1 + 2 summed
        userBasket.Items.Should().Contain(i => i.ProductId == _colaId && i.Quantity == 1);  // moved over

        // The anonymous basket was soft-deleted — and it persisted: the global IsDeleted
        // query filter now hides it, so a normal lookup by id returns null. (Against the old
        // AsNoTracking code the soft-delete was dropped and this basket would still be visible.)
        var anon = await context.Baskets.FirstOrDefaultAsync(b => b.Id == anonBasketId);
        anon.Should().BeNull("the anonymous basket should be soft-deleted and hidden by the global filter after the merge");
    }
}
