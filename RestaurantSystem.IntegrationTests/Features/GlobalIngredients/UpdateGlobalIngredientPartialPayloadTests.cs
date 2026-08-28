using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.GlobalIngredients.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.GlobalIngredients;

/// <summary>
/// backend #428 — a PUT that OMITS a field must not write that field.
///
/// <para>
/// <c>UpdateGlobalIngredientDto</c> carried two properties that a partial payload could not leave
/// alone: <c>isActive</c> (a non-nullable <c>bool</c>, so an absent field bound <c>false</c>) and
/// <c>kind</c> (defaulted to <see cref="IngredientKind.Ingredient"/>, so an absent field reclassified
/// a sauce). The first is the dangerous one, because <b>no screen lists an inactive library row</b> —
/// <c>GetGlobalIngredientsQuery</c> and <c>SearchGlobalIngredientsQuery</c> both require
/// <c>IsActive</c>, and the S3 archive drawer keys on <c>ArchivedAt</c>, so it does not catch these
/// rows either. The row simply left the catalogue with no way back through the API.
/// </para>
///
/// <para>
/// The two halves of the contract are equally load-bearing and are both pinned here: <b>absent</b>
/// means unchanged, <b>present</b> still means what it says. A fix that only preserved would have
/// broken deactivation, which is a real feature.
/// </para>
/// </summary>
[Collection("Database Lane 4")]
public class UpdateGlobalIngredientPartialPayloadTests : IntegrationTestBase
{
    private const string ShelfName = "#428 Shelf Ingredient";
    private const string SauceName = "#428 Shelf Sauce";

    private Guid _ingredientId;
    private Guid _sauceId;

    public UpdateGlobalIngredientPartialPayloadTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    // ---- absent means unchanged ----------------------------------------------------------------

    /// <summary>
    /// THE issue, in one test. It fails against the old DTO: the omitted field bound <c>false</c>,
    /// the handler assigned it unconditionally, and the row vanished from the only list there is.
    /// </summary>
    [Fact]
    public async Task UpdatingAnIngredientWithoutIsActive_LeavesItOnTheShelf()
    {
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"/api/global-ingredients/{_ingredientId}", new
        {
            defaultName = ShelfName + " (renamed)",
            translations = Array.Empty<object>(),
        });

        response.EnsureSuccessStatusCode();

        var shelf = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>("/api/global-ingredients");
        shelf!.Data!.Should().Contain(i => i.Id == _ingredientId,
            "omitting isActive says nothing about availability, so it must change nothing about it");

        (await FindAsync(_ingredientId))!.IsActive.Should().BeTrue();
    }

    /// <summary>
    /// The second door onto the same shelf. A row the picker cannot find is as lost as one the list
    /// cannot show, and the type-ahead applies the same <c>IsActive</c> filter.
    /// </summary>
    [Fact]
    public async Task UpdatingAnIngredientWithoutIsActive_LeavesItFindableInSearch()
    {
        AuthenticateAsAdmin();

        await PutAsJsonAsync($"/api/global-ingredients/{_ingredientId}", new
        {
            defaultName = ShelfName,
            translations = Array.Empty<object>(),
        });

        var found = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>(
            $"/api/global-ingredients/search?query={Uri.EscapeDataString(ShelfName)}");

        found!.Data!.Should().Contain(i => i.Id == _ingredientId);
    }

    /// <summary>
    /// The same defect on the same PUT, one property along: a sauce is a TYPED ingredient (S5), and
    /// the update DTO's <c>= IngredientKind.Ingredient</c> initialiser meant any caller that did not
    /// know about <c>kind</c> quietly demoted it back to an ordinary ingredient.
    /// </summary>
    [Fact]
    public async Task UpdatingASauceWithoutKind_LeavesItASauce()
    {
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"/api/global-ingredients/{_sauceId}", new
        {
            defaultName = SauceName,
            translations = Array.Empty<object>(),
        });

        response.EnsureSuccessStatusCode();

        (await FindAsync(_sauceId))!.Kind.Should().Be(IngredientKind.Sauce,
            "an omitted kind must not reclassify the row");
    }

    // ---- present still means what it says ------------------------------------------------------

    [Fact]
    public async Task UpdatingAnIngredientWithIsActiveFalse_StillTakesItOffTheShelf()
    {
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"/api/global-ingredients/{_ingredientId}", new
        {
            defaultName = ShelfName,
            isActive = false,
            translations = Array.Empty<object>(),
        });

        response.EnsureSuccessStatusCode();

        (await FindAsync(_ingredientId))!.IsActive.Should().BeFalse(
            "deactivation is a real feature — preserving on absence must not disable it");

        var shelf = await GetFromJsonAsync<ApiResponse<List<GlobalIngredientDto>>>("/api/global-ingredients");
        shelf!.Data!.Should().NotContain(i => i.Id == _ingredientId);
    }

    [Fact]
    public async Task UpdatingAnIngredientWithAnExplicitKind_StillReclassifiesIt()
    {
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"/api/global-ingredients/{_ingredientId}", new
        {
            defaultName = ShelfName,
            kind = "sauce",
            translations = Array.Empty<object>(),
        });

        response.EnsureSuccessStatusCode();

        (await FindAsync(_ingredientId))!.Kind.Should().Be(IngredientKind.Sauce);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<GlobalIngredient?> FindAsync(Guid id)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.GlobalIngredients.FirstOrDefaultAsync(g => g.Id == id);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var ingredient = new GlobalIngredient
        {
            DefaultName = ShelfName,
            IsActive = true,
            Kind = IngredientKind.Ingredient,
            CreatedBy = "test",
        };

        var sauce = new GlobalIngredient
        {
            DefaultName = SauceName,
            IsActive = true,
            Kind = IngredientKind.Sauce,
            CreatedBy = "test",
        };

        context.GlobalIngredients.AddRange(ingredient, sauce);
        await context.SaveChangesAsync();

        _ingredientId = ingredient.Id;
        _sauceId = sauce.Id;
    }
}
