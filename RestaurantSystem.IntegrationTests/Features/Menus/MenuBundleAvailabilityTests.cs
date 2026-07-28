using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

/// <summary>
/// §9.2 — bundles had no per-order-type availability at either end: <c>MenuBundleDto</c> was never
/// wired to <c>OrderTypeAvailability</c>, and no bundle command accepted a mask, so a bundle's
/// channel set was not merely unwired but UNSTORABLE. The browse grid rendered every combo as fully
/// orderable and the guest found out at the till.
/// <para>
/// Everything here goes over HTTP rather than through the mediator, because the two failure modes
/// this feature keeps producing are both invisible to a handler-level test: an unbound
/// <c>[FromQuery]</c> leaves the channel at null (permissive), and a missing
/// <c>ProductCategories → Category</c> include makes an INHERITING bundle resolve as UNRESTRICTED
/// (also permissive). Neither throws. The seeded bundles cover both halves — one owns its mask, one
/// inherits — so removing the include reddens the inheriting cases while leaving the rest green,
/// which is exactly the separation §9.15 asked for.
/// </para>
/// </summary>
public class MenuBundleAvailabilityTests : IntegrationTestBase
{
    private const string OwnMaskBundleName = "§9.2 Takeaway-Only Combo";
    private const string InheritingBundleName = "§9.2 Inheriting Combo";
    private const string UnrestrictedBundleName = "§9.2 Anywhere Combo";
    private const int TakeawayAndDelivery = (int)(OrderChannels.Takeaway | OrderChannels.Delivery);

    private Guid _inheritingBundleId;

    public MenuBundleAvailabilityTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task List_OnARefusedChannel_BlocksTheBundleWithItsOwnMask()
    {
        var bundle = await FetchListedAsync(OwnMaskBundleName, "DineIn");

        bundle.Availability.CanOrder.Should().BeFalse("the bundle's own mask refuses dine-in");
        bundle.Availability.Reason.Should().Be(AvailabilityReason.WrongOrderType);
        bundle.Availability.AllowedOrderTypes.Should().BeEquivalentTo(new[] { OrderType.Takeaway, OrderType.Delivery });
        bundle.Availability.InheritsOrderTypes.Should().BeFalse();
    }

    [Fact]
    public async Task List_OnARefusedChannel_BlocksTheBundleThatINHERITSTheMask()
    {
        // The include-sensitive case: with `ProductCategories -> Category` unloaded this reads as
        // unrestricted and reports `true` — a permissive verdict, silently.
        var bundle = await FetchListedAsync(InheritingBundleName, "DineIn");

        bundle.Availability.CanOrder.Should().BeFalse("the primary category refuses dine-in");
        bundle.Availability.InheritsOrderTypes.Should().BeTrue("the bundle carries no mask of its own");
        bundle.AvailableOrderTypes.Should().BeNull();
    }

    [Fact]
    public async Task List_OnAnAllowedChannel_LeavesEveryBundleOrderable()
    {
        var own = await FetchListedAsync(OwnMaskBundleName, "Takeaway");
        var inherited = await FetchListedAsync(InheritingBundleName, "Takeaway");

        own.Availability.CanOrder.Should().BeTrue();
        inherited.Availability.CanOrder.Should().BeTrue();
    }

    [Fact]
    public async Task List_WithNoChannelChosen_IsPermissiveButStillNamesTheRestriction()
    {
        // §4.4's dominant browse state: nothing is blocked, and the chip is driven by the allowed
        // set rather than by the verdict.
        var bundle = await FetchListedAsync(OwnMaskBundleName, requestedOrderType: null);

        bundle.Availability.CanOrder.Should().BeTrue();
        bundle.Availability.AllowedOrderTypes.Should().BeEquivalentTo(new[] { OrderType.Takeaway, OrderType.Delivery });
    }

    [Fact]
    public async Task List_LeavesAnUnrestrictedBundleAloneOnEveryChannel()
    {
        var bundle = await FetchListedAsync(UnrestrictedBundleName, "DineIn");

        bundle.Availability.CanOrder.Should().BeTrue();
        bundle.Availability.AllowedOrderTypes.Should().HaveCount(3);
    }

    /// <summary>
    /// The detail endpoint is a second entry point — the customization sheet opens from it — and
    /// §9.10's rule is that two surfaces must not disagree about one item. Compared STRUCTURALLY
    /// against the list rather than against hardcoded expectations, so a verdict that later diverges
    /// on one surface only (the deferred bundle↔child rule would land on the list first) fails here
    /// instead of being quietly re-baselined in two places.
    /// </summary>
    [Fact]
    public async Task Detail_AgreesWithTheList()
    {
        var listed = await FetchListedAsync(InheritingBundleName, "DineIn");
        var detail = await GetFromJsonAsync<ApiResponse<MenuBundleDto>>(
            $"/api/Menus/{_inheritingBundleId}?RequestedOrderType=DineIn");

        detail!.Data!.Availability.Should().BeEquivalentTo(listed.Availability);
        detail.Data.Availability.CanOrder.Should().BeFalse(
            "both surfaces must refuse it — an equivalence check alone passes when BOTH are permissive");
    }

    /// <summary>
    /// The create half of the write path, which nothing in the suite exercised at all before this —
    /// there was no <c>POST /api/Menus</c> test, so dropping the assignment in the create handler
    /// left every test green while the update handler's identical line was covered.
    /// </summary>
    [Fact]
    public async Task Create_StoresTheBundlesOwnMask()
    {
        AuthenticateAsAdmin();
        var body = BundleCreateBody("§9.2 Created Combo", availableOrderTypes: TakeawayAndDelivery);

        var response = await PostAsJsonAsync("/api/Menus", body);

        response.EnsureSuccessStatusCode();
        var created = await ReadResponseAsync<ApiResponse<ProductDto>>(response);
        created!.Success.Should().BeTrue(created.Message);
        (await StoredMaskAsync(created.Data!.Id)).Should().Be(TakeawayAndDelivery);
    }

    [Fact]
    public async Task Create_RejectsAMaskOutsideTheChannelRange()
    {
        AuthenticateAsAdmin();
        var body = BundleCreateBody("§9.2 Rejected Combo", availableOrderTypes: 8);

        var response = await PostAsJsonAsync("/api/Menus", body);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest,
            "8 is outside the three-channel mask, so it would block every channel");
    }

    /// <summary>
    /// The write half. Before this slice the mask was unstorable, so the read half above could only
    /// ever have been exercised by hand-editing the database.
    /// </summary>
    [Fact]
    public async Task Update_StoresTheBundlesOwnMask()
    {
        AuthenticateAsAdmin();
        var body = await BundleUpdateBodyAsync(_inheritingBundleId, availableOrderTypes: (int)OrderChannels.DineIn);

        var response = await PutAsJsonAsync($"/api/Menus/{_inheritingBundleId}", body);

        response.EnsureSuccessStatusCode();
        (await ReadResponseAsync<ApiResponse<ProductDto>>(response))!.Success.Should().BeTrue();
        (await StoredMaskAsync(_inheritingBundleId)).Should().Be((int)OrderChannels.DineIn);
    }

    /// <summary>
    /// The §9.1 landmine, restated for bundles: this is a full-replace PUT, so a writer that omits
    /// the field CLEARS the restriction. That is the accepted contract — null is how "unrestricted"
    /// is expressed — and the guard against it is that the one writer always echoes the field. Pinned
    /// so the semantics are a decision rather than an accident.
    /// </summary>
    [Fact]
    public async Task Update_WithTheFieldOmitted_ClearsTheMask()
    {
        AuthenticateAsAdmin();
        var seeded = await SeedBundleAsync("§9.2 Echo Test Combo", ownMask: (int)OrderChannels.Takeaway);
        var body = await BundleUpdateBodyAsync(seeded, availableOrderTypes: null);
        body.Remove("availableOrderTypes");

        (await PutAsJsonAsync($"/api/Menus/{seeded}", body)).EnsureSuccessStatusCode();

        (await StoredMaskAsync(seeded)).Should().BeNull();
    }

    [Fact]
    public async Task Update_RejectsAMaskOutsideTheChannelRange()
    {
        AuthenticateAsAdmin();
        var body = await BundleUpdateBodyAsync(_inheritingBundleId, availableOrderTypes: 0);

        var response = await PutAsJsonAsync($"/api/Menus/{_inheritingBundleId}", body);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest,
            "0 means orderable on no channel — a blocked item with no stateable reason");
    }

    /// <summary>
    /// PascalCase, matching what the client actually sends (`getPublicMenuBundles`, and its
    /// `getProducts`/`getFeaturedSpecial` siblings). ASP.NET binds query keys case-insensitively, but
    /// a test that exercises a different spelling from production is testing a different request.
    /// </summary>
    private async Task<MenuBundleDto> FetchListedAsync(string name, string? requestedOrderType)
    {
        var channel = requestedOrderType is null ? string.Empty : $"&RequestedOrderType={requestedOrderType}";
        var response = await GetFromJsonAsync<ApiResponse<PagedResult<MenuBundleDto>>>(
            $"/api/Menus?page=1&pageSize=50{channel}");
        return response!.Data!.Items.Single(b => b.Name == name);
    }

    private static Dictionary<string, object?> BundleCreateBody(string name, int? availableOrderTypes) => new()
    {
        ["name"] = name,
        ["description"] = "created by MenuBundleAvailabilityTests",
        ["basePrice"] = 30.00m,
        ["isActive"] = true,
        ["isAvailable"] = true,
        ["isSpecial"] = false,
        ["preparationTimeMinutes"] = 0,
        ["displayOrder"] = 0,
        ["categoryIds"] = Array.Empty<Guid>(),
        ["primaryCategoryId"] = null,
        ["menuDefinition"] = AlwaysAvailableDefinition(),
        // The create handler enumerates Content without a null guard.
        ["content"] = new Dictionary<string, object?>(),
        ["availableOrderTypes"] = availableOrderTypes
    };

    private static Dictionary<string, object?> AlwaysAvailableDefinition() => new()
    {
        ["isAlwaysAvailable"] = true,
        ["availableMonday"] = true,
        ["availableTuesday"] = true,
        ["availableWednesday"] = true,
        ["availableThursday"] = true,
        ["availableFriday"] = true,
        ["availableSaturday"] = true,
        ["availableSunday"] = true,
        ["sections"] = Array.Empty<object>()
    };

    private async Task<Dictionary<string, object?>> BundleUpdateBodyAsync(Guid bundleId, int? availableOrderTypes)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bundle = await context.Products.AsNoTracking().FirstAsync(p => p.Id == bundleId);

        return new Dictionary<string, object?>
        {
            ["id"] = bundleId,
            ["name"] = bundle.Name,
            ["description"] = bundle.Description,
            ["basePrice"] = bundle.BasePrice,
            ["isActive"] = true,
            ["isAvailable"] = true,
            ["isSpecial"] = false,
            ["preparationTimeMinutes"] = 0,
            ["displayOrder"] = 0,
            // Deliberately absent, as the bundle editor sends it: an empty list means "no category
            // instruction", so the existing assignments (primary flag included) survive.
            ["categoryIds"] = Array.Empty<Guid>(),
            ["primaryCategoryId"] = null,
            ["menuDefinition"] = AlwaysAvailableDefinition(),
            ["content"] = new Dictionary<string, object?>(),
            ["availableOrderTypes"] = availableOrderTypes
        };
    }

    private async Task<int?> StoredMaskAsync(Guid bundleId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await context.Products.AsNoTracking().FirstAsync(p => p.Id == bundleId)).AvailableOrderTypes;
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        await SeedBundleAsync(OwnMaskBundleName, ownMask: TakeawayAndDelivery);
        await SeedBundleAsync(UnrestrictedBundleName, ownMask: null);
        _inheritingBundleId = await SeedBundleAsync(
            InheritingBundleName, ownMask: null, inheritedMask: TakeawayAndDelivery);
    }

    private async Task<Guid> SeedBundleAsync(string name, int? ownMask, int? inheritedMask = null)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var bundle = new Product
        {
            Name = name,
            BasePrice = 25.00m,
            Type = ProductType.Menu,
            IsActive = true,
            IsAvailable = true,
            AvailableOrderTypes = ownMask,
            // The list query filters on `MenuDefinition != null` plus the schedule.
            MenuDefinition = new MenuDefinition { IsAlwaysAvailable = true, CreatedBy = "test" },
            CreatedBy = "test"
        };

        if (inheritedMask is not null)
        {
            bundle.ProductCategories.Add(new ProductCategory
            {
                Category = new Category
                {
                    Name = $"{name} Category",
                    AvailableOrderTypes = inheritedMask,
                    CreatedBy = "test"
                },
                IsPrimary = true,
                CreatedBy = "test"
            });
        }

        context.Add(bundle);
        await context.SaveChangesAsync();
        return bundle.Id;
    }
}
