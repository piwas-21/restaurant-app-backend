using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net.Http.Json;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

// Issue #190: UpdateMenuBundleCommandHandler removed a bundle's categories and descriptions
// BEFORE checking whether the command actually carried replacements, so a null or empty
// CategoryIds/Content deleted every row and re-added none. No client could avoid it — the
// RemoveRange ran ahead of the guard, so omitting the key wiped just as an empty list did.
// The frontend cannot populate either field for a bundle (the bundle form has no category
// control and MenuBundleDto returns none), so every update through here dropped them.
//
// Scope, precisely: this endpoint has always been live via MenuBundleDetails (which sends no
// categoryIds), so the wipe was reachable before frontend #213 routed the bundle EDIT MODAL
// here too — that path previously 400'd on the product endpoint. No RUMI data was actually
// lost: prod has no bundles, and UI-created bundles carry no categories because
// CreateMenuBundleModal ignores its categoryId prop. So this is a latent-defect fix, not a
// post-incident one — but it had to land before the unified editor (PR2d) exposes categories.
//
// These pin the corrected semantics: absent/empty means "no instruction", not "clear".
// The rule is not invented here — the validation block a few lines above the mutation already
// read `if (command.CategoryIds?.Any() == true)`, i.e. empty already meant "nothing to
// validate"; the fix simply makes the mutation path agree with it. UpdateProductCommandHandler
// guards Content identically ("treat that as 'no translation changes'").
[Collection("Database Lane 1")]
public class UpdateMenuBundlePreservesAssignmentsTests : IntegrationTestBase
{
    private Guid _bundleId;
    private Guid _categoryA;
    private Guid _categoryB;

    public UpdateMenuBundlePreservesAssignmentsTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var categories = await context.Categories.OrderBy(c => c.Name).Take(2).ToListAsync();
        _categoryA = categories[0].Id;
        _categoryB = categories[1].Id;

        var bundle = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Combo",
            BasePrice = 20m,
            Type = ProductType.Menu,
            IsActive = true,
            IsAvailable = true,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _bundleId = bundle.Id;

        // A bundle that HAS categories + translations — the state the wipe destroyed.
        bundle.ProductCategories.Add(new ProductCategory
        {
            ProductId = bundle.Id,
            CategoryId = _categoryA,
            IsPrimary = true,
            DisplayOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        bundle.Descriptions.Add(new ProductDescription
        {
            ProductId = bundle.Id,
            Lang = "fr",
            Name = "Menu Combo",
            Description = "Un combo",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        bundle.MenuDefinition = new MenuDefinition
        {
            ProductId = bundle.Id,
            IsAlwaysAvailable = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };

        context.Products.Add(bundle);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// The payload the admin UI actually sends for a bundle edit. primaryCategoryId defaults to
    /// null because the bundle form cannot set one; the validator requires any non-null primary to
    /// be present in CategoryIds, so it may only be supplied alongside a non-empty list.
    /// </summary>
    private object UpdatePayload(object? categoryIds, object? content, Guid? primaryCategoryId = null) => new
    {
        id = _bundleId,
        name = "Combo Renamed",
        description = "changed",
        basePrice = 22m,
        isActive = true,
        isAvailable = true,
        isSpecial = false,
        preparationTimeMinutes = 15,
        displayOrder = 0,
        categoryIds,
        primaryCategoryId,
        menuDefinition = new { isAlwaysAvailable = true, sections = Array.Empty<object>() },
        content
    };

    private async Task<(List<ProductCategory> Categories, List<string> Languages, string Name)> ReadBundleAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bundle = await context.Products
            .Include(p => p.ProductCategories)
            .Include(p => p.Descriptions)
            .AsNoTracking()
            .FirstAsync(p => p.Id == _bundleId);

        return (bundle.ProductCategories.ToList(),
                bundle.Descriptions.Select(d => d.Lang).ToList(),
                bundle.Name);
    }

    // The exact payload the admin bundle form produces — categoryIds: [] because the schema has
    // no category field. Before the fix this deleted the assignment and re-added nothing.
    [Fact]
    public async Task EmptyCategoryIds_PreservesExistingCategories()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Menus/{_bundleId}",
            UpdatePayload(Array.Empty<Guid>(), new Dictionary<string, object>
            {
                ["fr"] = new { name = "Menu Combo", description = "Un combo" }
            }),
            JsonOptions);

        response.EnsureSuccessStatusCode();

        var (categories, _, name) = await ReadBundleAsync();
        categories.Select(c => c.CategoryId).Should().BeEquivalentTo(new[] { _categoryA });
        categories.Single().IsPrimary.Should().BeTrue("the surviving assignment keeps its primary flag");
        name.Should().Be("Combo Renamed", "the rest of the update must still apply");
    }

    // MenuBundleDetails omits the key entirely. The RemoveRange ran before the null check, so
    // this wiped too — there was no payload that meant "leave categories alone".
    [Fact]
    public async Task OmittedCategoryIds_PreservesExistingCategories()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Menus/{_bundleId}",
            UpdatePayload(null, new Dictionary<string, object>
            {
                ["fr"] = new { name = "Menu Combo", description = "Un combo" }
            }),
            JsonOptions);

        response.EnsureSuccessStatusCode();

        var (categories, _, _) = await ReadBundleAsync();
        categories.Select(c => c.CategoryId).Should().BeEquivalentTo(new[] { _categoryA });
        categories.Single().IsPrimary.Should().BeTrue();
    }

    // The capability must survive the fix: a real list still replaces.
    [Fact]
    public async Task NonEmptyCategoryIds_ReplacesCategories()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Menus/{_bundleId}",
            UpdatePayload(new[] { _categoryB }, new Dictionary<string, object>
            {
                ["fr"] = new { name = "Menu Combo", description = "Un combo" }
            },
            primaryCategoryId: _categoryB),
            JsonOptions);

        response.EnsureSuccessStatusCode();

        var (categories, _, _) = await ReadBundleAsync();
        categories.Select(c => c.CategoryId).Should().BeEquivalentTo(new[] { _categoryB }, "a non-empty list is a replace instruction");
        // Nothing in the suite asserted IsPrimary/DisplayOrder before — mutating either survived
        // all 266 tests. The fix re-indents this exact block, so pin what it writes.
        var replaced = categories.Single();
        replaced.IsPrimary.Should().BeTrue("categoryB was sent as the primary");
        replaced.DisplayOrder.Should().Be(0, "displayOrder still counts from 0 inside the guard");
    }

    // Empty content wiped every translation, where the same action on a product is a no-op
    // (UpdateProductCommandHandler guards with `if (contentMap.Any())`). Now consistent.
    [Fact]
    public async Task EmptyContent_PreservesExistingDescriptions()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Menus/{_bundleId}",
            UpdatePayload(Array.Empty<Guid>(), new Dictionary<string, object>()),
            JsonOptions);

        response.EnsureSuccessStatusCode();

        var (_, languages, _) = await ReadBundleAsync();
        languages.Should().BeEquivalentTo(new[] { "fr" });
    }

    // Content is non-nullable on the command and the handler enumerated it directly, so an
    // omitted key NRE'd -> 500. The frontend was padding `{}` to work around exactly this.
    [Fact]
    public async Task OmittedContent_PreservesExistingDescriptions_AndDoesNotThrow()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Menus/{_bundleId}",
            UpdatePayload(Array.Empty<Guid>(), null),
            JsonOptions);

        response.EnsureSuccessStatusCode();

        var (_, languages, _) = await ReadBundleAsync();
        languages.Should().BeEquivalentTo(new[] { "fr" });
    }

    // The capability must survive: real content still replaces.
    [Fact]
    public async Task NonEmptyContent_ReplacesDescriptions()
    {
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(
            $"/api/Menus/{_bundleId}",
            UpdatePayload(Array.Empty<Guid>(), new Dictionary<string, object>
            {
                ["de"] = new { name = "Menü Combo", description = "Ein Combo" }
            }),
            JsonOptions);

        response.EnsureSuccessStatusCode();

        var (_, languages, _) = await ReadBundleAsync();
        languages.Should().BeEquivalentTo(new[] { "de" }, "a non-empty map is a replace instruction");
    }
}
