using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.GlobalIngredients;

/// <summary>
/// §9.18 — <c>DeleteGlobalIngredientCommand</c> intended a soft delete and performed a permanent one.
/// It called <c>Remove()</c> under the comment <i>"Soft delete handled by entity type configuration"</i>;
/// nothing handled it. <c>ApplicationDbContext</c> does convert <c>EntityState.Deleted</c> into
/// <c>IsDeleted</c> — in <c>ApplyAuditInformation()</c>, which at the time was called only from the
/// SYNCHRONOUS <c>SaveChanges()</c> override. Every handler awaits <c>SaveChangesAsync</c>, so it
/// never ran. (§9.18's root fix has since overridden <c>SaveChangesAsync</c> too, so the conversion
/// now works on every save; this test pins the handler's explicit soft delete regardless.)
/// <para>
/// <b>Why this needed a test rather than inspection.</b> The obvious assertion — "the ingredient is
/// gone from the list" — passed both before and after the fix, because a hard-deleted row and a
/// soft-deleted row are equally absent from a filtered read. Only <c>IgnoreQueryFilters()</c> tells
/// the two apart, which is why the defect survived: from every surface the admin can see, a
/// permanent delete and a soft delete are indistinguishable. The distinguishing assertions here are
/// the row's survival, its translations' survival (the FK cascaded them away), and the referenced
/// case below.
/// </para>
/// <para>
/// <b>The referenced case is a live bug, not just a data-retention one.</b> Deleting an ingredient
/// that any product actually used did not silently destroy data — it threw, and the admin got a 500
/// on an ordinary action with no way to complete it. That is the half of §9.18 a user would have
/// reported, and it is measured below, not inferred.
/// </para>
/// <para>
/// <b>Why it threw is worth stating precisely, because the obvious reading is wrong.</b> The store
/// constraint <c>fk_product_ingredients_global_ingredients_global_ingredient_id</c> is declared with
/// no <c>onDelete</c> — but on an OPTIONAL relationship (<c>ProductIngredient.GlobalIngredientId</c>
/// is <c>Guid?</c>, and the model snapshot configures neither <c>OnDelete</c> nor <c>IsRequired</c>)
/// EF Core's default is <c>ClientSetNull</c>, which nulls the FK on any dependent it is TRACKING
/// before the statement is ever sent. So the throw was never a structural guarantee: it happened
/// because this particular handler loads only the ingredient and no <c>ProductIngredients</c>, which
/// left the store constraint to reject the DELETE. A handler that had them tracked would instead
/// have silently unlinked live products' ingredients. Neither outcome is wanted; the fix removes
/// both by never issuing the DELETE.
/// </para>
/// </summary>
public class DeleteGlobalIngredientCommandTests : IntegrationTestBase
{
    private const string UnusedName = "§9.18 Unused Ingredient";
    private const string UsedName = "§9.18 Used Ingredient";
    private const string ProductName = "§9.18 Product With A Global Ingredient";

    private Guid _unusedId;
    private Guid _usedId;
    private Guid _productId;

    public DeleteGlobalIngredientCommandTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>
    /// THE regression test. Pre-fix the row was not flagged but GONE — absent even with the query
    /// filter disabled.
    /// </summary>
    [Fact]
    public async Task DeletingAnIngredient_FlagsTheRowInsteadOfDestroyingIt()
    {
        await DeleteAsync(_unusedId);

        var ingredient = await FindIncludingDeletedAsync(_unusedId);

        ingredient.Should().NotBeNull("a soft delete leaves the row behind");
        ingredient!.IsDeleted.Should().BeTrue();
        ingredient.DeletedAt.Should().NotBeNull();
        ingredient.DeletedBy.Should().Be(TestAuthHandler.AdminUserId,
            "the deleting admin is stamped via ICurrentUserService.GetAuditIdentifier()");
    }

    /// <summary>
    /// The translations hung off the ingredient by a CASCADE foreign key, so the hard delete took
    /// them with it. They are not soft-deletable themselves, so nothing could have restored them.
    /// </summary>
    [Fact]
    public async Task DeletingAnIngredient_LeavesItsTranslationsIntact()
    {
        await DeleteAsync(_unusedId);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var translations = await context.GlobalIngredientTranslations
            .Where(t => t.GlobalIngredientId == _unusedId)
            .ToListAsync();

        translations.Should().HaveCount(2, "the cascade that removed them no longer fires");
    }

    /// <summary>
    /// The user-visible half of §9.18: this action used to fail outright, because the FK is NO ACTION
    /// and a product still pointed at the row.
    /// </summary>
    [Fact]
    public async Task DeletingAnIngredientAProductStillUses_Succeeds()
    {
        var result = await DeleteAsync(_usedId);

        result.Success.Should().BeTrue(result.Message);
    }

    /// <summary>
    /// The other side of that: the product's own ingredient row must survive the delete with its link
    /// intact. Both pre-fix outcomes would have broken it — the store constraint's rejection blocked
    /// the delete entirely, and EF's <c>ClientSetNull</c> would have nulled the FK on any dependent it
    /// happened to be tracking. Nothing issues a DELETE now, so neither can happen.
    /// </summary>
    [Fact]
    public async Task DeletingAUsedIngredient_LeavesTheProductsOwnIngredientRowLinked()
    {
        await DeleteAsync(_usedId);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var productIngredient = await context.ProductIngredients
            .SingleAsync(pi => pi.ProductId == _productId);

        productIngredient.GlobalIngredientId.Should().Be(_usedId);
        productIngredient.Name.Should().Be("Tomato", "the local fallback name is what renders now");
    }

    [Fact]
    public async Task ADeletedIngredientDisappearsFromTheList()
    {
        await DeleteAsync(_unusedId);

        var response = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>("/api/global-ingredients");

        response!.Data!.Should().NotContain(i => i.DefaultName == UnusedName);
        response.Data.Should().Contain(i => i.DefaultName == UsedName, "only the deleted one is hidden");
    }

    /// <summary>
    /// Deleting twice must report "not found" rather than re-deleting: the handler's lookup goes
    /// through the global query filter, so it can no longer see the row. Pre-fix this was vacuous —
    /// the row was physically gone.
    /// </summary>
    [Fact]
    public async Task DeletingAnAlreadyDeletedIngredient_ReportsNotFound()
    {
        await DeleteAsync(_unusedId);
        var afterFirst = await FindIncludingDeletedAsync(_unusedId);

        var second = await DeleteAsync(_unusedId);

        second.Success.Should().BeFalse();

        // `Success == false` alone would also hold if the handler re-found the row and re-stamped it
        // — which is the "rather than re-deleting" half of the claim, and the half a filtered read
        // cannot see. Pinning the stamp is what makes this test non-vacuous.
        var afterSecond = await FindIncludingDeletedAsync(_unusedId);
        afterSecond!.DeletedAt.Should().Be(afterFirst!.DeletedAt);
        afterSecond.DeletedBy.Should().Be(afterFirst.DeletedBy);
    }

    /// <summary>
    /// <c>GetProductByIdQuery</c> runs <c>IgnoreQueryFilters()</c>, which un-filters its includes — the
    /// §9.14 shape. Soft-deleting a global ingredient is what makes it reachable here for the first
    /// time, so the guard ships with this fix. The ingredient still renders under its local name; only
    /// the deleted global's translations are withheld.
    /// </summary>
    [Fact]
    public async Task ProductDetail_DoesNotServeADeletedGlobalsTranslations()
    {
        await DeleteAsync(_usedId);

        var detail = await GetFromJsonAsync<ApiResponse<ProductDto>>($"/api/Products/{_productId}");

        var ingredient = detail!.Data!.DetailedIngredients!.Single();
        ingredient.Name.Should().Be("Tomato", "the row itself is unaffected by the global's deletion");
        ingredient.Content.Should().BeEmpty("the translations came from the now-deleted global");
    }

    /// <summary>
    /// The control. Without it, a guard that dropped the translations unconditionally would satisfy
    /// the test above.
    /// </summary>
    [Fact]
    public async Task ProductDetail_StillServesALiveGlobalsTranslations()
    {
        var detail = await GetFromJsonAsync<ApiResponse<ProductDto>>($"/api/Products/{_productId}");

        var ingredient = detail!.Data!.DetailedIngredients!.Single();
        ingredient.Content.Should().ContainKey("en");
        ingredient.Content["en"].Name.Should().Be("Tomato (EN)");
    }

    private async Task<ApiResponse<string>> DeleteAsync(Guid id)
    {
        AuthenticateAsAdmin();
        var response = await Client.DeleteAsync($"/api/global-ingredients/{id}");
        response.EnsureSuccessStatusCode();
        return (await ReadResponseAsync<ApiResponse<string>>(response))!;
    }

    private async Task<GlobalIngredient?> FindIncludingDeletedAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.GlobalIngredients
            // soft-delete-bypass: the whole point of this class is to tell a flagged row apart from a
            // destroyed one, which no filtered read can do.
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var unused = new GlobalIngredient
        {
            DefaultName = UnusedName,
            IsActive = true,
            CreatedBy = "test",
            Translations =
            {
                new GlobalIngredientTranslation { LanguageCode = "en", Name = "Unused (EN)", CreatedBy = "test" },
                new GlobalIngredientTranslation { LanguageCode = "fr", Name = "Unused (FR)", CreatedBy = "test" },
            },
        };

        var used = new GlobalIngredient
        {
            DefaultName = UsedName,
            IsActive = true,
            CreatedBy = "test",
            Translations =
            {
                new GlobalIngredientTranslation { LanguageCode = "en", Name = "Tomato (EN)", CreatedBy = "test" },
            },
        };

        var product = new Product
        {
            Name = ProductName,
            BasePrice = 9.50m,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.MainItem,
            CreatedBy = "test",
        };
        // The state that made the pre-fix delete THROW: a live product pointing at the ingredient
        // across a NO ACTION foreign key.
        product.DetailedIngredients.Add(new ProductIngredient
        {
            Name = "Tomato",
            IsActive = true,
            GlobalIngredient = used,
            CreatedBy = "test",
        });

        context.AddRange(unused, used, product);
        await context.SaveChangesAsync();

        _unusedId = unused.Id;
        _usedId = used.Id;
        _productId = product.Id;
    }
}
