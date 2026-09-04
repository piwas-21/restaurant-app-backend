using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

/// <summary>
/// Backend #477 — a bundle's OWN allergens were never served. <c>MenuBundleDto</c> had no
/// <c>Allergens</c> property at all, and <c>MenuBundleMapper</c>'s single allergen line maps a
/// SECTION ITEM's, so a labelled combo reached the guest indistinguishable from an unlabelled one.
///
/// <para>
/// The reason this is pinned over HTTP and in both directions is that the failure is silent and
/// PERMISSIVE. The guest filter decides exclusion by absence — an item with no tokens cannot
/// contain the token being excluded — so an unserved label does not hide the combo, it lists it
/// under "No gluten". Measured on MC FOOD: 45 labelled bundles, 45 cards showing nothing.
/// </para>
///
/// <para>
/// Both queries are exercised because they are separate call sites of the mapper and used to
/// disagree about what they projected — unifying them is the whole reason
/// <c>MenuBundleMapper</c> exists. A test through one proves nothing about the other.
/// </para>
/// </summary>
[Collection("Database Lane 1")]
public class MenuBundleAllergenTests : IntegrationTestBase
{
    private const string LabelledBundleName = "#477 Labelled Combo";
    private const string UnlabelledBundleName = "#477 Unlabelled Combo";

    /// <summary>
    /// Deliberately DIFFERENT from the option's, so the assertions can tell the bundle's own row
    /// from its children's. Mapping the child's allergens onto the bundle would be a plausible
    /// wrong fix that a same-value fixture could not distinguish from the right one.
    /// </summary>
    private static readonly string[] BundleAllergens = ["gluten", "sesame"];
    private static readonly string[] OptionAllergens = ["milk"];

    private Guid _labelledBundleId;
    private Guid _unlabelledBundleId;

    public MenuBundleAllergenTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task The_list_serves_the_bundles_own_allergens()
    {
        var bundle = await FetchListedAsync(LabelledBundleName);

        bundle.Allergens.Should().BeEquivalentTo(BundleAllergens,
            "the guest filter reads absence as 'free of everything', so an unserved label lists a "
            + "gluten combo under 'No gluten'");
    }

    [Fact]
    public async Task The_detail_serves_the_bundles_own_allergens()
    {
        var bundle = await FetchByIdAsync(_labelledBundleId);

        bundle.Allergens.Should().BeEquivalentTo(BundleAllergens);
    }

    /// <summary>
    /// The discrimination control. The bundle and its option carry DIFFERENT allergens, so a fix
    /// that reached for the child's list — the one line that was already in the mapper — cannot
    /// satisfy this.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_bundles_allergens_are_its_own_not_its_options(bool viaDetail)
    {
        var bundle = viaDetail ? await FetchByIdAsync(_labelledBundleId) : await FetchListedAsync(LabelledBundleName);

        var option = bundle.MenuDefinition!.Sections.Single().Items.Single();

        bundle.Allergens.Should().BeEquivalentTo(BundleAllergens);
        option.Allergens.Should().BeEquivalentTo(OptionAllergens,
            "the option's own labelling still maps — this fix adds a level, it does not move one");
    }

    /// <summary>
    /// The independent control: an unlabelled bundle is still RETURNED, by both queries. This is
    /// the one assertion here that stays green when the new mapping line is deleted, and goes red
    /// only if something starts hiding unlabelled combos — which is the change this fix must not
    /// become. An earlier version asserted <c>BeEmpty()</c> instead; that failed under the same
    /// mutation as everything else, so it discriminated nothing.
    /// </summary>
    [Fact]
    public async Task An_unlabelled_bundle_is_still_listed_and_still_readable()
    {
        (await FetchListedAsync(UnlabelledBundleName)).Id.Should().Be(_unlabelledBundleId);
        (await FetchByIdAsync(_unlabelledBundleId)).Name.Should().Be(UnlabelledBundleName);
    }

    /// <summary>
    /// The wire shape for an unlabelled bundle, asserted on the raw JSON rather than the
    /// deserialised DTO — which cannot tell `null` from `[]` once `List&lt;string&gt;?` has absorbed
    /// both.
    /// <para>
    /// It matters because <c>null</c> is the ONLY shape production produces. `Product.Allergens`
    /// has no initialiser and no bundle command assigns it, so every bundle created through the
    /// admin path holds SQL NULL — never an empty array. A test seeded with `[]` would be pinning
    /// a state the platform never reaches. The client half must therefore handle `null`
    /// (`item.allergens ?? []`), and this is the assertion that says so out loud.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_unlabelled_bundle_serves_null_rather_than_a_fabricated_empty_list()
    {
        var response = await Client.GetAsync($"/api/Menus/{_unlabelledBundleId}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var allergens = json.RootElement.GetProperty("data").GetProperty("allergens");

        allergens.ValueKind.Should().Be(JsonValueKind.Null,
            "the mapper passes the stored value through; inventing [] here would hide from the "
            + "client that an unlabelled row is null, which is what it actually has to handle");
    }

    private async Task<MenuBundleDto> FetchListedAsync(string name)
    {
        var response = await GetFromJsonAsync<ApiResponse<PagedResult<MenuBundleDto>>>(
            "/api/Menus?page=1&pageSize=50");
        return response!.Data!.Items.Single(b => b.Name == name);
    }

    private async Task<MenuBundleDto> FetchByIdAsync(Guid id)
    {
        var response = await GetFromJsonAsync<ApiResponse<MenuBundleDto>>($"/api/Menus/{id}");
        return response!.Data!;
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        _labelledBundleId = await SeedBundleAsync(LabelledBundleName, [.. BundleAllergens]);
        // NULL, not [] — the only shape the platform actually produces (see the wire-shape test).
        _unlabelledBundleId = await SeedBundleAsync(UnlabelledBundleName, null);
    }

    private async Task<Guid> SeedBundleAsync(string name, List<string>? allergens)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var option = new Product
        {
            Name = $"{name} Option",
            BasePrice = 3m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            Allergens = [.. OptionAllergens],
            CreatedBy = "test"
        };

        var section = new MenuSection
        {
            Name = "Drink",
            DisplayOrder = 1,
            IsRequired = true,
            MinSelection = 1,
            MaxSelection = 1,
            CreatedBy = "test"
        };
        section.Items.Add(new MenuSectionItem
        {
            Product = option,
            AdditionalPrice = 0m,
            DisplayOrder = 1,
            IsDefault = true,
            CreatedBy = "test"
        });

        var definition = new MenuDefinition { IsAlwaysAvailable = true, CreatedBy = "test" };
        definition.Sections.Add(section);

        var bundle = new Product
        {
            Name = name,
            BasePrice = 25.00m,
            Type = ProductType.Menu,
            IsActive = true,
            IsAvailable = true,
            Allergens = allergens,
            // The list query filters on `MenuDefinition != null` plus the schedule.
            MenuDefinition = definition,
            CreatedBy = "test"
        };

        context.Add(bundle);
        await context.SaveChangesAsync();
        return bundle.Id;
    }
}
