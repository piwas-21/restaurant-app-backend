using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Features.Basket.Interfaces;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

/// <summary>
/// kebabdilhan G5 — <b>"Tacos Double Viandes: choose exactly 2 meats out of 6"</b>, as the ADD PATH
/// sees it. The 6 meats are COMPONENT products (<c>Product.IsComponent</c>): real rows a bundle
/// section can reference, that no guest may order on their own.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BasketComponentGuardTests"/> pins the decision; this pins the WIRING plus the thing
/// the wiring must NOT break. A guard placed one level too deep would reject the bundle's own
/// chosen options and make the feature impossible; a guard never called at all throws nothing and
/// looks correct in a unit test. Both failure modes are asserted here, in the same fixture, because
/// only the PAIR is evidence — either assertion alone is satisfied by a broken implementation.
/// </para>
/// <para>
/// It also exercises the column end to end (entity + EF config + migration), so a flag that never
/// reaches the database fails here rather than in production.
/// </para>
/// </remarks>
[Collection("Database Lane 3")]
public class BasketComponentProductTests : IntegrationTestBase
{
    public BasketComponentProductTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private const string Actor = "g5-test";

    private static readonly Guid TacosId = Guid.NewGuid();
    private static readonly Guid SectionId = Guid.NewGuid();
    private static readonly Guid PlainProductId = Guid.NewGuid();

    /// <summary>The 6 meats. Six is the real number from the tenant's menu, and it matters: the
    /// section rule under test is "exactly 2 OUT OF 6", not "exactly 2 of the only 2 there are".</summary>
    private static readonly Guid[] MeatIds =
        [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    private readonly string _sessionId = Guid.NewGuid().ToString();

    // ---- the refusal ---------------------------------------------------------------------------

    [Fact]
    public async Task A_meat_ordered_on_its_own_is_refused()
    {
        var thrown = await Record.ExceptionAsync(() => AddAsync(new AddToBasketDto
        {
            ProductId = MeatIds[0],
            Quantity = 1,
        }));

        thrown.Should().BeOfType<BadRequestException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.ComponentNotOrderable);
        (await LineCountAsync()).Should().Be(0, "a refused add must leave no line behind");
    }

    /// <summary>
    /// The guard reads a STORED column, so it must not depend on the product being hidden anywhere
    /// else. Excluding components from the catalogue queries hides the card; the id is still
    /// published inside every bundle's <c>MenuDefinition</c>, which is exactly how a caller would
    /// come to hold it.
    /// </summary>
    [Fact]
    public async Task The_refusal_does_not_depend_on_the_product_being_inactive()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meat = await context.Products.FirstAsync(p => p.Id == MeatIds[0]);
            meat.IsActive.Should().BeTrue("a component stays active — it must remain choosable inside the bundle");
            meat.IsAvailable.Should().BeTrue();
        }

        var thrown = await Record.ExceptionAsync(() => AddAsync(new AddToBasketDto
        {
            ProductId = MeatIds[0],
            Quantity = 1,
        }));

        thrown.Should().BeOfType<BadRequestException>();
    }

    [Fact]
    public async Task An_ordinary_product_is_still_addable()
    {
        // The negative control for the guard: it must reject components and nothing else. Without
        // this, a guard that threw for EVERY product would pass every other test in this class.
        var thrown = await Record.ExceptionAsync(() => AddAsync(new AddToBasketDto
        {
            ProductId = PlainProductId,
            Quantity = 1,
        }));

        thrown.Should().BeNull();
        (await LineCountAsync()).Should().Be(1);
    }

    // ---- the feature the refusal must not break ------------------------------------------------

    [Fact]
    public async Task The_same_meats_ARE_choosable_inside_the_bundle()
    {
        // The whole point. The guard is called on the TOP-LEVEL product only; a bundle's options
        // are resolved inside BasketItemFactory and never reach it. Move the call one level deeper
        // and "choose 2 meats" becomes unorderable.
        var thrown = await Record.ExceptionAsync(() => AddTacosAsync(MeatIds[0], MeatIds[3]));

        thrown.Should().BeNull();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var parent = await context.BasketItems
            .Include(bi => bi.ChildBasketItems)
            .SingleAsync(bi => bi.ProductId == TacosId);

        parent.ChildBasketItems.Should().HaveCount(2, "the guest chose two meats");
        parent.ChildBasketItems.Select(c => c.ProductId)
            .Should().BeEquivalentTo(new[] { MeatIds[0], MeatIds[3] });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Anything_other_than_two_meats_is_refused_by_the_section_rule(int chosen)
    {
        // "EXACTLY 2" is MinSelection = MaxSelection = 2 on the section, and it is enforced on the
        // SERVER (BasketItemFactory.ValidateSectionsAndSumOptionPrices) — not by the picker UI.
        // Both bounds, because a rule that only has a floor is a different rule.
        var thrown = await Record.ExceptionAsync(() => AddTacosAsync(MeatIds.Take(chosen).ToArray()));

        thrown.Should().BeOfType<BadRequestException>();
        (await LineCountAsync()).Should().Be(0, "nothing is persisted when a section rule refuses");
    }

    /// <summary>
    /// <b>The stated residual, measured rather than assumed: "2 x the SAME meat" is not expressible
    /// through the shipped picker.</b> A section counts SELECTION ENTRIES, so one entry carrying
    /// <c>quantity: 2</c> is ONE selection and is refused by <c>MinSelection = 2</c> — and the
    /// picker emits exactly one entry per chosen option, so it has no way to send two. This test
    /// pins the behaviour so the limit is a recorded fact, not a surprise; it is NOT a fix, and
    /// G8/G9 (a general min/max engine) remains out of scope pending an owner decision.
    /// </summary>
    [Fact]
    public async Task Double_of_one_meat_is_not_expressible_as_a_single_selection()
    {
        var thrown = await Record.ExceptionAsync(() => AddAsync(new AddToBasketDto
        {
            ProductId = TacosId,
            Quantity = 1,
            SelectedMenuOptions =
            [
                new SelectedMenuOptionDto { SectionId = SectionId, ItemId = MeatIds[0], Quantity = 2 },
            ],
        }));

        thrown.Should().BeOfType<BadRequestException>(
            "quantity 2 on one entry is still ONE selection, and the section needs two");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private Task AddTacosAsync(params Guid[] meatIds) => AddAsync(new AddToBasketDto
    {
        ProductId = TacosId,
        Quantity = 1,
        SelectedMenuOptions = meatIds
            .Select(id => new SelectedMenuOptionDto { SectionId = SectionId, ItemId = id, Quantity = 1 })
            .ToList(),
    });

    private async Task AddAsync(AddToBasketDto request)
    {
        using var scope = Factory.Services.CreateScope();
        var basketService = scope.ServiceProvider.GetRequiredService<IBasketService>();
        await basketService.AddItemToBasketAsync(_sessionId, null, request);
    }

    private async Task<int> LineCountAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.BasketItems.CountAsync(bi => bi.ParentBasketItemId == null);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var category = new Category { Name = "G5 Tacos", AvailableOrderTypes = null, CreatedBy = Actor };

        var tacos = NewProduct(TacosId, "Tacos Double Viandes", ProductType.Menu, category, isComponent: false);
        var plain = NewProduct(PlainProductId, "G5 Coca", ProductType.Beverage, category, isComponent: false);

        var section = new MenuSection
        {
            Id = SectionId,
            Name = "Choisissez 2 viandes",
            IsRequired = true,
            MinSelection = 2,
            MaxSelection = 2,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor,
        };

        var meats = new List<Product>();
        for (var i = 0; i < MeatIds.Length; i++)
        {
            var meat = NewProduct(MeatIds[i], $"G5 Viande {i + 1}", ProductType.MainItem, category, isComponent: true);
            meats.Add(meat);
            section.Items.Add(new MenuSectionItem
            {
                Id = Guid.NewGuid(),
                ProductId = meat.Id,
                AdditionalPrice = 0m,
                DisplayOrder = i,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = Actor,
            });
        }

        var definition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = tacos.Id,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor,
        };
        definition.Sections.Add(section);

        context.AddRange(meats);
        context.AddRange(tacos, plain);
        context.Add(definition);
        await context.SaveChangesAsync();
    }

    private static Product NewProduct(Guid id, string name, ProductType type, Category category, bool isComponent)
    {
        var product = new Product
        {
            Id = id,
            Name = name,
            BasePrice = 9m,
            // A component is ACTIVE and AVAILABLE on purpose. The design must not lean on the
            // "a bundle child's active state is never checked" hole: deactivating the meats would
            // hide them by accident and make this feature depend on a separate latent defect.
            IsActive = true,
            IsAvailable = true,
            IsComponent = isComponent,
            Type = type,
            AvailableOrderTypes = null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor,
        };
        product.ProductCategories.Add(new ProductCategory { Category = category, IsPrimary = true, CreatedBy = Actor });
        return product;
    }
}
