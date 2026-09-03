using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalVariations.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.GlobalVariations;

/// <summary>
/// The variation library (plan S4): the same catalog shape as the ingredient one, which by now has
/// survived a picker (S2) and an archive state (S3).
///
/// <para>
/// What is genuinely different, and therefore what these tests are about: a variation's per-product
/// fact is its <c>PriceModifier</c> — +2.00 for a large pizza, +0.50 for a large coffee — so the
/// catalog carries names and translations and NEVER a price. A pick copies nine translations and
/// leaves the money to the product.
/// </para>
/// </summary>
[Collection("Database Lane 3")]
public class GlobalVariationLibraryTests : IntegrationTestBase
{
    private const string UsedName = "S4 Used Variation";
    private const string UnusedName = "S4 Unused Variation";
    private const string SeededName = "S4 Built-in Variation";

    private Guid _usedId;
    private Guid _unusedId;
    private Guid _seededId;
    private Guid _productId;

    public GlobalVariationLibraryTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ---- the shelf -----------------------------------------------------------------------------

    [Fact]
    public async Task TheLibrary_ServesEveryLiveRowWithItsTranslations()
    {
        var response = await GetFromJsonAsync<ApiResponse<List<GlobalVariationDto>>>("/api/global-variations");

        var used = response!.Data!.Single(v => v.DefaultName == UsedName);
        used.Translations.Should().Contain(t => t.LanguageCode == "fr" && t.Name == "Grand");
        used.IsArchived.Should().BeFalse();
    }

    /// <summary>
    /// The catalog holds no price on purpose: the same "Large" is +2.00 on a pizza and +0.50 on a
    /// coffee. If a price ever appears on this DTO, this test is the one that should have to change
    /// first.
    /// </summary>
    [Fact]
    public async Task TheLibraryRow_CarriesNoPrice()
    {
        var response = await GetFromJsonAsync<ApiResponse<List<GlobalVariationDto>>>("/api/global-variations");

        typeof(GlobalVariationDto).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(name => name.Contains("Price", StringComparison.OrdinalIgnoreCase));
        response!.Data.Should().NotBeEmpty();
    }

    // ---- the reverse link ----------------------------------------------------------------------

    [Fact]
    public async Task UsageCount_IsTheNumberOfProductsThatCopiedTheRow()
    {
        var response = await GetFromJsonAsync<ApiResponse<List<GlobalVariationDto>>>("/api/global-variations");

        var library = response!.Data!;
        library.Single(v => v.DefaultName == UsedName).UsedOnProductCount.Should().Be(1);
        library.Single(v => v.DefaultName == UnusedName).UsedOnProductCount.Should().Be(0);
    }

    // ---- archive / restore ---------------------------------------------------------------------

    /// <summary>
    /// D4, inherited from S3: a row a product uses is archived, and archiving leaves that product
    /// exactly as it was — the name it renders is its OWN copy, so this must hold even though
    /// nothing about the product changed.
    /// </summary>
    [Fact]
    public async Task DeletingAVariationAProductUses_ArchivesIt_AndLeavesTheProductLinked()
    {
        var result = await DeleteAsync(_usedId);

        result.Success.Should().BeTrue(result.Message);

        var row = await FindIncludingDeletedAsync(_usedId);
        row!.ArchivedAt.Should().NotBeNull();
        row.ArchivedBy.Should().Be(TestAuthHandler.AdminUserId);
        row.IsDeleted.Should().BeFalse("an archived row is not a deleted one");

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var variation = await context.ProductVariations.SingleAsync(v => v.ProductId == _productId);
        variation.GlobalVariationId.Should().Be(_usedId);
        variation.Name.Should().Be("Large");
    }

    [Fact]
    public async Task DeletingATenantsOwnVariationNoProductUses_SoftDeletesIt()
    {
        await DeleteAsync(_unusedId);

        var row = await FindIncludingDeletedAsync(_unusedId);
        row!.IsDeleted.Should().BeTrue();
        row.ArchivedAt.Should().BeNull("nothing referenced it, so there was nothing to archive");
    }

    /// <summary>
    /// D14. These catalogs are per-tenant TABLES seeded with platform rows, so an unused built-in
    /// was indistinguishable from a name the admin typed and the picker offered "Delete" on all
    /// fifty. A built-in is archived at ANY usage count — including zero, which is the case above.
    /// </summary>
    [Fact]
    public async Task DeletingABuiltInVariation_ArchivesItInsteadOfRemovingIt()
    {
        var result = await DeleteAsync(_seededId);

        result.Success.Should().BeTrue(result.Message);

        var row = await FindIncludingDeletedAsync(_seededId);
        row!.IsDeleted.Should().BeFalse("a built-in is never removed");
        row.ArchivedAt.Should().NotBeNull();
        row.ArchivedBy.Should().Be(TestAuthHandler.AdminUserId);
    }

    /// <summary>
    /// The shelf has to SAY which rows are which, or the picker cannot hide the destructive control
    /// for the built-ins — and the server rule above would be the only thing between an admin and a
    /// button that no longer does what it says.
    /// </summary>
    [Fact]
    public async Task TheLibrary_SaysWhichRowsAreBuiltInAndWhichAreTheTenantsOwn()
    {
        var response = await GetFromJsonAsync<ApiResponse<List<GlobalVariationDto>>>("/api/global-variations");

        response!.Data!.Single(v => v.DefaultName == SeededName).Origin.Should().Be(LibraryOrigin.System);
        response.Data!.Single(v => v.DefaultName == UnusedName).Origin.Should().Be(LibraryOrigin.Custom);
    }

    /// <summary>
    /// A row the picker itself creates is the tenant's own, and therefore removable. The column
    /// defaults to System so the seeded rows need no backfill, which makes the create handler's
    /// stamp the whole of what separates the two shelves.
    /// </summary>
    [Fact]
    public async Task ARowCreatedThroughThePicker_IsTheTenantsOwn()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync(
            "/api/global-variations",
            new CreateGlobalVariationDto { DefaultName = "S4 Typed In The Picker" });
        var created = await ReadResponseAsync<ApiResponse<GlobalVariationDto>>(response);

        created!.Data!.Origin.Should().Be(LibraryOrigin.Custom);
    }

    [Fact]
    public async Task AnArchivedVariation_LeavesTheShelfAndAppearsInTheDrawer()
    {
        await DeleteAsync(_usedId);

        var shelf = await GetFromJsonAsync<ApiResponse<List<GlobalVariationDto>>>("/api/global-variations");
        shelf!.Data!.Should().NotContain(v => v.Id == _usedId);

        AuthenticateAsAdmin();
        var drawer = await GetFromJsonAsync<ApiResponse<List<GlobalVariationDto>>>("/api/global-variations/archived");
        var archived = drawer!.Data!.Single(v => v.Id == _usedId);
        archived.IsArchived.Should().BeTrue();
        archived.UsedOnProductCount.Should().Be(1);
    }

    [Fact]
    public async Task TheArchiveDrawer_IsNotServedToAGuest()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync("/api/global-variations/archived");

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task RestoringAnArchivedVariation_PutsItBackOnTheShelf()
    {
        await DeleteAsync(_usedId);

        var restored = await RestoreAsync(_usedId);

        restored.Success.Should().BeTrue(restored.Message);
        restored.Data!.IsArchived.Should().BeFalse();
        restored.Data.UsedOnProductCount.Should().Be(1);

        var shelf = await GetFromJsonAsync<ApiResponse<List<GlobalVariationDto>>>("/api/global-variations");
        shelf!.Data!.Should().Contain(v => v.Id == _usedId);
    }

    [Fact]
    public async Task ArchivingTwice_IsRefused_AndKeepsTheFirstStamp()
    {
        await DeleteAsync(_usedId);
        var afterFirst = await FindIncludingDeletedAsync(_usedId);

        var second = await DeleteAsync(_usedId);

        second.Success.Should().BeFalse();
        (await FindIncludingDeletedAsync(_usedId))!.ArchivedAt.Should().Be(afterFirst!.ArchivedAt);
    }

    // ---- create / edit -------------------------------------------------------------------------

    [Fact]
    public async Task CreatingAVariation_StoresItsTranslations()
    {
        AuthenticateAsAdmin();

        var response = await PostAsJsonAsync("/api/global-variations", new
        {
            defaultName = "  50 cl  ",
            translations = new[] { new { languageCode = "fr", name = "50 cl" }, new { languageCode = "de", name = "50 cl" } },
        });

        var created = (await ReadResponseAsync<ApiResponse<GlobalVariationDto>>(response))!;
        created.Success.Should().BeTrue(created.Message);
        created.Data!.DefaultName.Should().Be("50 cl", "the name is trimmed");
        created.Data.Translations.Should().HaveCount(2);
        created.Data.UsedOnProductCount.Should().Be(0);
    }

    /// <summary>
    /// backend #428, not repeated here: on the ingredient library <c>isActive</c> is a non-nullable
    /// bool, so a PUT that merely omits it binds <c>false</c> and hides the row from every screen
    /// with no way to find it again. This table takes <c>bool?</c> and preserves.
    /// </summary>
    [Fact]
    public async Task UpdatingWithoutIsActive_LeavesTheRowOnTheShelf()
    {
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"/api/global-variations/{_unusedId}", new
        {
            defaultName = "S4 Unused Variation (renamed)",
            translations = Array.Empty<object>(),
        });

        response.EnsureSuccessStatusCode();

        var shelf = await GetFromJsonAsync<ApiResponse<List<GlobalVariationDto>>>("/api/global-variations");
        shelf!.Data!.Should().Contain(v => v.Id == _unusedId,
            "omitting isActive must say nothing about availability, not deactivate the row");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<ApiResponse<string>> DeleteAsync(Guid id)
    {
        AuthenticateAsAdmin();
        var response = await Client.DeleteAsync($"/api/global-variations/{id}");
        response.EnsureSuccessStatusCode();
        return (await ReadResponseAsync<ApiResponse<string>>(response))!;
    }

    private async Task<ApiResponse<GlobalVariationDto>> RestoreAsync(Guid id)
    {
        AuthenticateAsAdmin();
        var response = await Client.PostAsync($"/api/global-variations/{id}/restore", content: null);
        response.EnsureSuccessStatusCode();
        return (await ReadResponseAsync<ApiResponse<GlobalVariationDto>>(response))!;
    }

    private async Task<GlobalVariation?> FindIncludingDeletedAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.GlobalVariations
            // soft-delete-bypass: half these assertions are about telling an archived row apart from
            // a deleted one, which no filtered read can do.
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var used = new GlobalVariation
        {
            DefaultName = UsedName,
            IsActive = true,
            CreatedBy = "test",
            Translations =
            {
                new GlobalVariationTranslation { LanguageCode = "fr", Name = "Grand", CreatedBy = "test" },
                new GlobalVariationTranslation { LanguageCode = "de", Name = "Groß", CreatedBy = "test" },
            },
        };

        // The tenant's OWN unused row — what "Delete" is for. It is explicitly Custom because the
        // column defaults to System, and a System row is archived at any usage count (D14).
        var unused = new GlobalVariation
        {
            DefaultName = UnusedName,
            IsActive = true,
            Origin = LibraryOrigin.Custom,
            CreatedBy = "test",
        };

        // …and a platform-seeded one that nothing uses, which is the case the picker used to label
        // "Delete" on all fifty shipped rows.
        var seeded = new GlobalVariation
        {
            DefaultName = SeededName,
            IsActive = true,
            Origin = LibraryOrigin.System,
            CreatedBy = "test",
        };

        var product = new Product
        {
            Name = "S4 Product With A Library Variation",
            BasePrice = 12m,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.MainItem,
            CreatedBy = "test",
        };
        // The product's own copy: its own name, and its own price modifier, which the catalog never
        // carries.
        product.Variations.Add(new ProductVariation
        {
            Name = "Large",
            PriceModifier = 2.00m,
            IsActive = true,
            DisplayOrder = 0,
            GlobalVariation = used,
            CreatedBy = "test",
        });

        context.AddRange(used, unused, seeded, product);
        await context.SaveChangesAsync();

        _usedId = used.Id;
        _unusedId = unused.Id;
        _seededId = seeded.Id;
        _productId = product.Id;
    }
}
