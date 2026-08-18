using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Commands.UpdateProductCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>
/// §9.5 — nothing stopped a product carrying two PRIMARY categories, and
/// <c>OrderTypeAvailability.EffectiveMask</c> resolves inheritance through
/// <c>FirstOrDefault(pc =&gt; pc.IsPrimary)</c>. With two, a product's channel restriction depends on
/// row load order: the same item can be orderable on one request and refused on the next, with
/// nothing in the data to explain it.
/// </summary>
/// <remarks>
/// The migration's REPAIR half is tested by re-running its SQL against a state this suite creates
/// on purpose. A migration that only ever runs on a clean test database proves nothing about the
/// tenant database it was written for — the floor-plan S10 carry-over made the same point, where
/// the interesting branch could not fire on the test DB at all.
/// </remarks>
[Collection("Database Lane 2")]
public class PrimaryProductCategoryConstraintTests : IntegrationTestBase
{
    public PrimaryProductCategoryConstraintTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// The migration's repair statement, verbatim — asserted against the migration file itself by
    /// <see cref="TheRepairSqlUnderTest_IsTheOneTheMigrationActuallyRuns"/>. Migrations are immutable
    /// (CLAUDE.md §9), so a changed repair means a NEW migration; without that assertion this const
    /// would happily go on testing superseded SQL and stay green.
    private const string RepairSql = @"
        UPDATE product_categories pc
        SET is_primary = false
        WHERE pc.is_primary = true
          AND pc.id <> (
              SELECT keep.id
              FROM product_categories keep
              WHERE keep.product_id = pc.product_id
                AND keep.is_primary = true
              ORDER BY keep.display_order, keep.id
              LIMIT 1
          );";

    [Fact]
    public void TheRepairSqlUnderTest_IsTheOneTheMigrationActuallyRuns()
    {
        // Without this, `RepairSql` is a COPY that can drift from the migration silently — the test
        // would keep passing while the shipped statement did something else.
        var migrationsDir = Path.Combine(
            RepoRoot(), "RestaurantSystem.Infrastructure", "Persistence", "Migrations");
        var migration = Directory
            .EnumerateFiles(migrationsDir, "*_AddUniquePrimaryProductCategoryIndex.cs")
            .Single(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal));

        var text = File.ReadAllText(migration);

        // Compare whitespace-insensitively: the migration indents the SQL inside a verbatim string.
        Normalize(text).Should().Contain(Normalize(RepairSql),
            "the repair under test must be the repair that ships");
    }

    private static string Normalize(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    [Fact]
    public async Task TheIndex_RefusesASecondPrimaryCategoryForOneProduct()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var product = NewProduct("§9.5 Two Primaries");
        product.ProductCategories.Add(Link(NewCategory("§9.5 A"), isPrimary: true, displayOrder: 0));
        context.Add(product);
        await context.SaveChangesAsync();

        context.Add(new ProductCategory
        {
            ProductId = product.Id,
            Category = NewCategory("§9.5 B"),
            IsPrimary = true,
            DisplayOrder = 1,
            CreatedBy = "test",
        });

        var act = async () => await context.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>(
            "the database — not just the app — must make two primaries unrepresentable");
    }

    [Fact]
    public async Task TheIndex_AllowsAsManySECONDARYCategoriesAsYouLike()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var product = NewProduct("§9.5 One Primary, Many Secondaries");
        product.ProductCategories.Add(Link(NewCategory("§9.5 P"), isPrimary: true, displayOrder: 0));
        product.ProductCategories.Add(Link(NewCategory("§9.5 S1"), isPrimary: false, displayOrder: 1));
        product.ProductCategories.Add(Link(NewCategory("§9.5 S2"), isPrimary: false, displayOrder: 2));
        context.Add(product);

        var act = async () => await context.SaveChangesAsync();

        await act.Should().NotThrowAsync("the index is FILTERED to primary rows for exactly this reason");
    }

    [Fact]
    public async Task TheMigrationsRepair_DemotesEveryExtraPrimary_Deterministically()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Build the pre-migration state the index would otherwise fail on. The index is already in
        // place here, so the rows go in via raw SQL — which is also how they got there on a real
        // database: nothing in the app ever prevented it.
        var product = NewProduct("§9.5 Repair Target");
        var keep = NewCategory("§9.5 Keep");
        var demote = NewCategory("§9.5 Demote");

        // A SECOND, healthy product. Without it the table holds only the corrupt pair, and dropping
        // `AND keep.product_id = pc.product_id` from the repair would still pass — while on a real
        // catalogue that mutation demotes every primary except one, globally.
        var bystander = NewProduct("§9.5 Bystander");
        var bystanderCategory = NewCategory("§9.5 Bystander Primary");
        bystander.ProductCategories.Add(Link(bystanderCategory, isPrimary: true, displayOrder: 5));

        context.AddRange(product, keep, demote, bystander);
        await context.SaveChangesAsync();

        var keepLinkId = Guid.NewGuid();
        var demoteLinkId = Guid.NewGuid();
        // try/finally: the fixture is ONE container shared by every test class in sequence
        // (the lane database outlives the test, and Respawn resets rows, not schema). Leaving the index
        // dropped after a failure here turns one real failure into a confusing cascade in unrelated
        // classes.
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                @"INSERT INTO product_categories (id, product_id, category_id, is_primary, display_order, created_at, created_by)
                  VALUES ({0}, {2}, {4}, true, 1, now(), 'test');
                  DROP INDEX ix_product_categories_product_id_is_primary_unique;
                  INSERT INTO product_categories (id, product_id, category_id, is_primary, display_order, created_at, created_by)
                  VALUES ({1}, {3}, {5}, true, 0, now(), 'test');",
                demoteLinkId, keepLinkId, product.Id, product.Id, demote.Id, keep.Id);

            await context.Database.ExecuteSqlRawAsync(RepairSql);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ix_product_categories_product_id_is_primary_unique
                  ON product_categories (product_id, is_primary) WHERE ""is_primary"" = true;");
        }

        var links = await context.ProductCategories
            .AsNoTracking()
            .Where(pc => pc.ProductId == product.Id)
            .ToListAsync();

        links.Should().HaveCount(2, "the repair demotes, it does not delete — a category link is data");
        links.Should().ContainSingle(pc => pc.IsPrimary, "exactly one primary must survive");
        // Lowest display_order wins, so the ambiguity resolves the way a sorted read would have.
        links.Single(pc => pc.IsPrimary).Id.Should().Be(keepLinkId);

        // The healthy product is untouched — the repair is per-product, and a no-op on clean data.
        var bystanderLinks = await context.ProductCategories
            .AsNoTracking()
            .Where(pc => pc.ProductId == bystander.Id)
            .ToListAsync();
        bystanderLinks.Should().ContainSingle(pc => pc.IsPrimary,
            "a product that was already correct must not be touched, whatever its display_order");

        // The `finally` above already re-created the unique index over the repaired data — which is
        // also the migration's real order, and it did not throw.
    }

    /// <summary>
    /// THE risk this index carries: `UpdateProductCommand` clears a product's category links and
    /// re-adds them inside ONE `SaveChanges`, so if EF ordered the INSERT before the DELETE the
    /// unique index would turn a routine admin save — moving the primary from category A to B —
    /// into a 500. It does not (EF Core 10 deletes first, and `ProductCategory` is hard-deleted,
    /// not soft-deleted, so the old row physically goes). This pins that, because the day it stops
    /// being true the failure lands on an admin, in production, on an ordinary edit.
    /// </summary>
    [Fact]
    public async Task RePointingAProductsPrimaryCategory_StillSavesWithTheIndexInPlace()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<CustomMediator>();

        var categoryA = NewCategory("§9.5 Re-point A");
        var categoryB = NewCategory("§9.5 Re-point B");
        var product = NewProduct("§9.5 Re-point Target");
        product.ProductCategories.Add(Link(categoryA, isPrimary: true, displayOrder: 0));
        product.ProductCategories.Add(Link(categoryB, isPrimary: false, displayOrder: 1));
        context.AddRange(categoryA, categoryB, product);
        await context.SaveChangesAsync();

        var command = new UpdateProductCommand(
            product.Id, product.Name, null, product.BasePrice, true, true, false, 10,
            ProductType.MainItem, KitchenType.None, null, null, 0,
            CategoryIds: [categoryA.Id, categoryB.Id],
            PrimaryCategoryId: categoryB.Id,
            Variations: null, SuggestedSideItemIds: null, DetailedIngredients: null,
            MenuDefinition: null, Content: null);

        var act = async () => await mediator.SendCommand<UpdateProductCommand, ApiResponse<ProductDto>>(command);

        await act.Should().NotThrowAsync("moving the primary is an ordinary admin edit");

        var links = await context.ProductCategories.AsNoTracking()
            .Where(pc => pc.ProductId == product.Id)
            .ToListAsync();
        links.Should().ContainSingle(pc => pc.IsPrimary);
        links.Single(pc => pc.IsPrimary).CategoryId.Should().Be(categoryB.Id, "the primary moved to B");
    }

    private static Product NewProduct(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        BasePrice = 10m,
        IsActive = true,
        IsAvailable = true,
        Type = ProductType.MainItem,
        CreatedBy = "test",
    };

    private static Category NewCategory(string name) => new() { Name = name, CreatedBy = "test" };

    private static ProductCategory Link(Category category, bool isPrimary, int displayOrder) => new()
    {
        Category = category,
        IsPrimary = isPrimary,
        DisplayOrder = displayOrder,
        CreatedBy = "test",
    };
}
