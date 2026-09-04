using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

/// <summary>
/// Backend #478 — a bundle's allergens had no write path, and adding one naively WIPES them.
///
/// <para>
/// The trap: the admin editor already put <c>allergens</c> into every bundle PUT before the
/// server read the field. So the moment it does, a save of anything else — a rename, a price
/// change — writes whatever the form held, and a form that never seeded the stored value holds
/// <c>[]</c>. MC FOOD has 45 labelled bundles.
/// </para>
///
/// <para>
/// For allergens that is a SAFETY regression, not a lost preference: <c>useMenuFilters</c> reads
/// an item with no tokens as free of everything, so a wiped combo is listed to a guest who asked
/// for "No gluten". Hence the contract asserted here: <c>null</c> means LEAVE ALONE, <c>[]</c>
/// means CLEAR. Those are different instructions, and the whole fix is the distinction.
/// </para>
/// </summary>
[Collection("Database Lane 4")]
public class UpdateMenuBundleAllergensTests : IntegrationTestBase
{
    private Guid _bundleId;

    public UpdateMenuBundleAllergensTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var bundle = new Product
        {
            Id = Guid.NewGuid(),
            Name = "#478 Labelled Combo",
            BasePrice = 20m,
            Type = ProductType.Menu,
            IsActive = true,
            IsAvailable = true,
            Ingredients = [],
            Allergens = ["gluten", "sesame"],
            MenuDefinition = new MenuDefinition { IsAlwaysAvailable = true, CreatedBy = "test" },
            CreatedBy = "test",
        };
        _bundleId = bundle.Id;
        context.Products.Add(bundle);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// The body the admin UI sends. `allergens` is OMITTED unless a test supplies it, which is
    /// exactly the older-client shape this contract has to survive.
    /// </summary>
    private object Payload(object? allergens = null) => new
    {
        id = _bundleId,
        name = "#478 Labelled Combo",
        description = "renamed, nothing to do with allergens",
        basePrice = 22m,
        isActive = true,
        isAvailable = true,
        isSpecial = false,
        preparationTimeMinutes = 15,
        displayOrder = 0,
        menuDefinition = new { isAlwaysAvailable = true, sections = Array.Empty<object>() },
        content = new Dictionary<string, object>(),
        allergens,
    };

    private async Task<List<string>?> StoredAllergensAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await context.Products.AsNoTracking().FirstAsync(p => p.Id == _bundleId)).Allergens;
    }

    [Fact]
    public async Task A_save_that_never_mentions_allergens_keeps_them()
    {
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"/api/Menus/{_bundleId}", Payload());

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await StoredAllergensAsync()).Should().BeEquivalentTo(["gluten", "sesame"],
            "an older client sends no allergens, and must not strip a labelled combo");
    }

    [Fact]
    public async Task An_explicit_list_is_written()
    {
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"/api/Menus/{_bundleId}", Payload(new[] { "milk", "nuts" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await StoredAllergensAsync()).Should().BeEquivalentTo(["milk", "nuts"]);
    }

    /// <summary>
    /// The other half of the contract, and the control against "never write allergens": an admin
    /// who unticks every chip is saying something, and an empty array is how they say it. A fix
    /// that treated `[]` like `null` would satisfy the preservation test above while making the
    /// field impossible to clear.
    /// </summary>
    [Fact]
    public async Task An_explicitly_EMPTY_list_clears_them()
    {
        AuthenticateAsAdmin();

        var response = await PutAsJsonAsync($"/api/Menus/{_bundleId}", Payload(Array.Empty<string>()));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await StoredAllergensAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// CREATE writes them too. Separate command, separate handler, and it assigns unconditionally
    /// because there is nothing stored to leave alone — so it needs its own assertion rather than
    /// inheriting confidence from the update path.
    /// </summary>
    [Fact]
    public async Task A_new_bundle_is_created_with_its_allergens()
    {
        AuthenticateAsAdmin();

        var response = await PostAsJsonAsync("/api/Menus", new
        {
            name = $"#478 Created Combo {Guid.NewGuid():N}",
            description = "created with labels",
            basePrice = 15m,
            isActive = true,
            isAvailable = true,
            isSpecial = false,
            preparationTimeMinutes = 10,
            displayOrder = 0,
            menuDefinition = new { isAlwaysAvailable = true, sections = Array.Empty<object>() },
            content = new Dictionary<string, object>(),
            allergens = new[] { "mustard" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var created = await ReadResponseAsync<Api.Common.Models.ApiResponse<Api.Features.Products.Dtos.ProductDto>>(response);
        created!.Data!.Allergens.Should().BeEquivalentTo(["mustard"]);
    }

    /// <summary>
    /// The endpoint carries <c>[ApiScope(MenuWrite)]</c>, so a machine token reaches it with none
    /// of the admin editor's sixteen chips in the way. The bound is on the shape that is never
    /// meaningful — blank, duplicated, absurdly long — not on the vocabulary, which a rule cannot
    /// police without freezing it.
    /// </summary>
    [Theory]
    [InlineData("blank")]
    [InlineData("duplicate")]
    [InlineData("toolong")]
    public async Task A_malformed_allergen_list_is_refused(string kind)
    {
        AuthenticateAsAdmin();
        string[] allergens = kind switch
        {
            "blank" => ["gluten", "   "],
            "duplicate" => ["gluten", "Gluten"],
            _ => [new string('x', 41)],
        };

        var response = await PutAsJsonAsync($"/api/Menus/{_bundleId}", Payload(allergens));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StoredAllergensAsync()).Should().BeEquivalentTo(["gluten", "sesame"],
            "a refused save must not have written anything");
    }

    /// <summary>
    /// The read side agrees with the write side. Serving is #477's job, but a round trip is what
    /// an admin actually experiences, and the two have been out of step before — the field was
    /// readable for a release while nothing could write it.
    /// </summary>
    [Fact]
    public async Task What_was_written_is_what_is_served()
    {
        AuthenticateAsAdmin();
        await PutAsJsonAsync($"/api/Menus/{_bundleId}", Payload(new[] { "celery" }));

        var served = await GetFromJsonAsync<Api.Common.Models.ApiResponse<Api.Features.Menus.Dtos.MenuBundleDto>>(
            $"/api/Menus/{_bundleId}");

        served!.Data!.Allergens.Should().BeEquivalentTo(["celery"]);
    }
}
