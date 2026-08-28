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
/// S3 / plan D4 — <i>"a catalog row in use is archived, never removed"</i>.
///
/// <para>
/// The catalog had exactly one way to leave the shelf: a soft delete, hidden by the global query
/// filter, therefore invisible to every read in the application — including the reference
/// navigation a product detail resolves. So deleting a library row a product had actually copied
/// silently emptied that ingredient's translated names, and nothing could put it back.
/// <c>DeleteGlobalIngredientCommandTests</c> pins that behaviour for a row already in that state;
/// this class pins the state the same action produces NOW, which is the opposite one: an archived
/// row is off the shelf but still readable, still serving the products that reference it, and
/// restorable.
/// </para>
///
/// <para>
/// The distinguishing assertions are therefore never "it is absent from the list" — a deleted row
/// and an archived row are equally absent from there, which is exactly how the old behaviour hid.
/// They are the columns on the row, the product's rendered translations, and the round trip back.
/// </para>
/// </summary>
[Collection("Database Lane 1")]
public class GlobalIngredientArchiveTests : IntegrationTestBase
{
    private const string UsedName = "S3 Used Ingredient";
    private const string UnusedName = "S3 Unused Ingredient";

    private Guid _categoryId;
    private Guid _usedId;
    private Guid _unusedId;
    private Guid _productId;

    public GlobalIngredientArchiveTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ---- the two outcomes of one DELETE --------------------------------------------------------

    /// <summary>
    /// THE slice, in one test: the row a product uses survives as an ARCHIVED row, not a deleted
    /// one. Asserting `IsDeleted == false` is the half that fails against the old handler, which
    /// flagged exactly that column.
    /// </summary>
    [Fact]
    public async Task DeletingAnIngredientAProductUses_ArchivesItInsteadOfDeletingIt()
    {
        var result = await DeleteAsync(_usedId);

        result.Success.Should().BeTrue(result.Message);

        var ingredient = await FindIncludingDeletedAsync(_usedId);
        ingredient!.ArchivedAt.Should().NotBeNull();
        ingredient.ArchivedBy.Should().Be(TestAuthHandler.AdminUserId,
            "the archiving admin is stamped via ICurrentUserService.GetAuditIdentifier()");
        ingredient.IsDeleted.Should().BeFalse("an archived row is not a deleted one");
    }

    /// <summary>
    /// D4's other half, and the reason this is a branch rather than a rename: a row NOTHING uses has
    /// no history to protect, so it is still soft-deleted exactly as §9.18 left it. The picker knows
    /// which of the two it will get, because it renders the same count.
    /// </summary>
    [Fact]
    public async Task DeletingAnIngredientNoProductUses_StillSoftDeletesIt()
    {
        await DeleteAsync(_unusedId);

        var ingredient = await FindIncludingDeletedAsync(_unusedId);
        ingredient!.IsDeleted.Should().BeTrue();
        ingredient.ArchivedAt.Should().BeNull("nothing referenced it, so there was nothing to archive");
    }

    // ---- what archiving must NOT break ---------------------------------------------------------

    /// <summary>
    /// The product that already copied the row keeps BOTH halves of what it had: the provenance link
    /// and the text. The text is the half a soft delete destroys — the same call on the same data
    /// leaves <c>Content</c> empty, which is what
    /// <c>DeleteGlobalIngredientCommandTests.ProductDetail_DoesNotServeADeletedGlobalsTranslations</c>
    /// pins — so this is the assertion that says archiving is a different state and not a rename.
    /// </summary>
    [Fact]
    public async Task ArchivingAUsedIngredient_LeavesTheProductRenderingItsTranslations()
    {
        await DeleteAsync(_usedId);

        var detail = await GetFromJsonAsync<ApiResponse<ProductDto>>($"/api/Products/{_productId}");

        var ingredient = detail!.Data!.DetailedIngredients!.Single();
        ingredient.GlobalIngredientId.Should().Be(_usedId, "the provenance is untouched");
        ingredient.Name.Should().Be("Tomato");
        ingredient.Content.Should().ContainKey("en");
        ingredient.Content["en"].Name.Should().Be("Tomato (EN)",
            "an archived row is off the shelf, not gone — the products that copied it still read it");
    }

    /// <summary>
    /// A product save must not lose the link on its next edit. #424 already carries an unchanged
    /// link forward unchecked, and S3 must not break that property: the provenance guard now also
    /// excludes archived rows from the ids it will accept, so this is the test that says it excludes
    /// them only from NEW links. <c>ProductIngredientProvenanceTests</c> pins the same pair from the
    /// product side.
    /// </summary>
    [Fact]
    public async Task ArchivingAnIngredient_SurvivesTheProductBeingSavedAgain()
    {
        await DeleteAsync(_usedId);

        var detail = await GetFromJsonAsync<ApiResponse<ProductDto>>($"/api/Products/{_productId}");
        var product = detail!.Data!;

        AuthenticateAsAdmin();
        var response = await PutAsJsonAsync($"/api/Products/{_productId}", new
        {
            id = _productId,
            name = product.Name,
            basePrice = product.BasePrice,
            isActive = true,
            isAvailable = true,
            isSpecial = false,
            preparationTimeMinutes = 10,
            type = "mainItem",
            kitchenType = "none",
            displayOrder = 0,
            categoryIds = new[] { _categoryId },
            primaryCategoryId = _categoryId,
            detailedIngredients = product.DetailedIngredients!.Select(i => new
            {
                id = i.Id,
                globalIngredientId = i.GlobalIngredientId,
                name = i.Name,
                isOptional = i.IsOptional,
                price = i.Price,
                isIncludedInBasePrice = i.IsIncludedInBasePrice,
                isActive = true,
                displayOrder = 0,
                maxQuantity = 1,
            }),
        });

        response.EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await context.ProductIngredients.SingleAsync(i => i.ProductId == _productId);

        stored.GlobalIngredientId.Should().Be(_usedId,
            "an already-stored link is carried forward unchecked — archiving must not erase it");
    }

    // ---- off the shelf -------------------------------------------------------------------------

    [Fact]
    public async Task AnArchivedIngredient_DisappearsFromTheLibrary()
    {
        await DeleteAsync(_usedId);

        var response = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>("/api/global-ingredients");

        response!.Data!.Should().NotContain(i => i.DefaultName == UsedName);
        response.Data.Should().Contain(i => i.DefaultName == UnusedName, "only the archived one is hidden");
    }

    /// <summary>
    /// The type-ahead is a second door onto the same shelf. Leaving it open would let an admin pick
    /// a row the write path then refuses, and the pick would save silently without its provenance.
    /// </summary>
    [Fact]
    public async Task AnArchivedIngredient_DisappearsFromSearch()
    {
        AuthenticateAsAdmin();
        var before = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>(
            $"/api/global-ingredients/search?query={Uri.EscapeDataString(UsedName)}");
        before!.Data!.Should().Contain(i => i.Id == _usedId, "the control: it is findable while on the shelf");

        await DeleteAsync(_usedId);

        var after = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>(
            $"/api/global-ingredients/search?query={Uri.EscapeDataString(UsedName)}");
        after!.Data!.Should().NotContain(i => i.Id == _usedId);
    }

    [Fact]
    public async Task AnArchivedIngredient_IsListedInTheArchiveDrawer()
    {
        await DeleteAsync(_usedId);

        AuthenticateAsAdmin();
        var response = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>("/api/global-ingredients/archived");

        var archived = response!.Data!.Single(i => i.Id == _usedId);
        archived.IsArchived.Should().BeTrue();
        archived.UsedOnProductCount.Should().Be(1, "the drawer says what is at stake in a restore");
        response.Data.Should().NotContain(i => i.DefaultName == UnusedName, "that one is on the shelf");
    }

    /// <summary>
    /// The drawer exists to undo an admin action, and no guest surface has a use for a row that is
    /// off the shelf. The list endpoint beside it is deliberately anonymous, so this is worth
    /// pinning rather than assuming.
    /// </summary>
    [Fact]
    public async Task TheArchiveDrawer_IsNotServedToAGuest()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync("/api/global-ingredients/archived");

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    // ---- reversibility -------------------------------------------------------------------------

    /// <summary>Archiving is only safe to offer because it is reversible. This is that claim.</summary>
    [Fact]
    public async Task RestoringAnArchivedIngredient_PutsItBackOnTheShelf()
    {
        await DeleteAsync(_usedId);

        var restored = await RestoreAsync(_usedId);

        restored.Success.Should().BeTrue(restored.Message);
        restored.Data!.IsArchived.Should().BeFalse();
        restored.Data.UsedOnProductCount.Should().Be(1);

        var row = await FindIncludingDeletedAsync(_usedId);
        row!.ArchivedAt.Should().BeNull();
        row.ArchivedBy.Should().BeNull("the stamp goes with the state it recorded");

        var library = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>("/api/global-ingredients");
        library!.Data!.Should().Contain(i => i.Id == _usedId);
    }

    /// <summary>
    /// Archiving twice would re-stamp who archived it and when, destroying the only record of the
    /// first one. Unlike a soft delete, the row is still visible to the handler, so the guard has to
    /// be explicit rather than fall out of the query filter.
    /// </summary>
    [Fact]
    public async Task ArchivingAnAlreadyArchivedIngredient_IsRefused_AndKeepsTheFirstStamp()
    {
        await DeleteAsync(_usedId);
        var afterFirst = await FindIncludingDeletedAsync(_usedId);

        var second = await DeleteAsync(_usedId);

        second.Success.Should().BeFalse();
        var afterSecond = await FindIncludingDeletedAsync(_usedId);
        afterSecond!.ArchivedAt.Should().Be(afterFirst!.ArchivedAt);
    }

    [Fact]
    public async Task RestoringARowThatWasNeverArchived_IsRefused()
    {
        var result = await RestoreAsync(_unusedId);

        result.Success.Should().BeFalse();
    }

    /// <summary>
    /// Restore undoes an ARCHIVE, not a delete: a soft-deleted row is behind the global query
    /// filter, and reading through it is what ADR-002 reserves for a purge. The asymmetry is
    /// deliberate and only ever applies to a row no product used.
    /// </summary>
    [Fact]
    public async Task RestoringASoftDeletedRow_ReportsNotFound()
    {
        await DeleteAsync(_unusedId);

        var result = await RestoreAsync(_unusedId);

        result.Success.Should().BeFalse();
        (await FindIncludingDeletedAsync(_unusedId))!.IsDeleted.Should().BeTrue();
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<ApiResponse<string>> DeleteAsync(Guid id)
    {
        AuthenticateAsAdmin();
        var response = await Client.DeleteAsync($"/api/global-ingredients/{id}");
        response.EnsureSuccessStatusCode();
        return (await ReadResponseAsync<ApiResponse<string>>(response))!;
    }

    private async Task<ApiResponse<GlobalIngredientDto>> RestoreAsync(Guid id)
    {
        AuthenticateAsAdmin();
        var response = await Client.PostAsync($"/api/global-ingredients/{id}/restore", content: null);
        response.EnsureSuccessStatusCode();
        return (await ReadResponseAsync<ApiResponse<GlobalIngredientDto>>(response))!;
    }

    private async Task<GlobalIngredient?> FindIncludingDeletedAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.GlobalIngredients
            // soft-delete-bypass: this class exists to tell an ARCHIVED row apart from a deleted
            // one, and half its assertions are about rows the filter hides.
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _categoryId = (await context.Categories.OrderBy(c => c.Name).FirstAsync()).Id;

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

        var unused = new GlobalIngredient
        {
            DefaultName = UnusedName,
            IsActive = true,
            CreatedBy = "test",
        };

        var product = new Product
        {
            Name = "S3 Product With A Library Ingredient",
            BasePrice = 9.50m,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.MainItem,
            CreatedBy = "test",
        };
        product.DetailedIngredients.Add(new ProductIngredient
        {
            Name = "Tomato",
            IsActive = true,
            MaxQuantity = 1,
            GlobalIngredient = used,
            CreatedBy = "test",
        });
        product.ProductCategories.Add(new ProductCategory
        {
            CategoryId = _categoryId,
            IsPrimary = true,
            CreatedBy = "test",
        });

        context.AddRange(used, unused, product);
        await context.SaveChangesAsync();

        _usedId = used.Id;
        _unusedId = unused.Id;
        _productId = product.Id;
    }
}
