using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Features.ApiTokens;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Menus;

/// <summary>
/// Bundle schedules over HTTP, on the tenant's wall clock (backend #397). Two things are pinned that
/// the predicate's own unit tests cannot see:
/// <list type="number">
/// <item>the LIST handler reads <c>ITenantClock</c> and not <c>DateTime.UtcNow</c> — the instant is
/// chosen so those two disagree about the hour AND the day, so the old code answers differently;</item>
/// <item>the by-id read applies the SAME filter, which it did not apply at all: the list hid a bundle
/// the detail endpoint then served in full, and that endpoint is what the guest customization sheet
/// opens.</item>
/// </list>
/// <para>
/// Europe/Paris, because that is the MC FOOD tenant's zone — the same +02:00 summer offset as Zurich,
/// so this is the production configuration and not a laboratory one.
/// </para>
/// </summary>
[Collection("Database Lane 3")]
public class MenuBundleScheduleTests : ApiTokenScopeTestBase
{
    /// <summary>Friday 21:30 UTC — 23:30 on Friday night in Paris, which is a different HOUR and,
    /// for anything after 22:00Z, would be a different DAY too.</summary>
    private static readonly DateTimeOffset Instant = new(2026, 7, 3, 21, 30, 0, TimeSpan.Zero);

    private const string LateNightName = "#397 Late-Night Combo";
    private const string LunchName = "#397 Lunch Formula";
    private const string AlwaysName = "#397 Anytime Combo";

    private Guid _lunchId;
    private Guid _lateNightId;

    public MenuBundleScheduleTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<ITenantClock>(new FixedTenantClock("Europe/Paris", Instant));
    }

    /// <summary>
    /// 23:30 local is inside Friday's 22:00-02:00 window. On <c>DateTime.UtcNow</c> the same instant
    /// reads 21:30, which is before the window opens — and a <c>&gt;= 22:00 &amp;&amp; &lt;= 02:00</c>
    /// test never matched at any hour anyway.
    /// </summary>
    [Fact]
    public async Task The_list_serves_a_late_night_bundle_on_the_tenant_wall_clock()
    {
        var names = await ListedNamesAsync();

        names.Should().Contain(LateNightName);
        names.Should().Contain(AlwaysName, "an always-available bundle is not scheduled at all");
    }

    [Fact]
    public async Task The_list_hides_a_bundle_whose_window_has_closed()
    {
        (await ListedNamesAsync()).Should().NotContain(LunchName, "lunch ended at 14:00 local");
    }

    /// <summary>
    /// The #397 disagreement itself. The list hides the lunch formula at 23:30; before this fix the
    /// by-id read had no schedule filter of any kind and served it in full to the same guest.
    /// </summary>
    [Fact]
    public async Task A_guest_cannot_open_a_bundle_the_list_hides()
    {
        AuthenticateAsAnonymous();

        var response = await Client.GetAsync($"/api/Menus/{_lunchId}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_guest_can_open_a_bundle_the_list_shows()
    {
        AuthenticateAsAnonymous();

        var detail = await GetFromJsonAsync<ApiResponse<MenuBundleDto>>($"/api/Menus/{_lateNightId}");

        detail!.Success.Should().BeTrue(detail.Message);
        detail.Data!.Name.Should().Be(LateNightName,
            "the negative case above must fail because of the SCHEDULE, not because this endpoint 404s for everything");
    }

    /// <summary>
    /// Staff are exempt, deliberately: the bundle editor loads through this endpoint, and the till
    /// sells what a guest asks for. A schedule filter here would make an out-of-window lunch menu
    /// un-editable and un-sellable — a worse defect than the one being fixed.
    /// </summary>
    [Fact]
    public async Task An_admin_can_still_open_an_out_of_window_bundle_to_edit_it()
    {
        AuthenticateAsAdmin();

        var detail = await GetFromJsonAsync<ApiResponse<MenuBundleDto>>($"/api/Menus/{_lunchId}");

        detail!.Success.Should().BeTrue(detail.Message);
        detail.Data!.Name.Should().Be(LunchName);
    }

    /// <summary>
    /// A machine API token is BACK-OF-HOUSE here, exactly as it is for a deactivated product on
    /// <c>GET /api/Products</c> (#438) — one caller class, one answer. The rule is written on
    /// <c>ICurrentUserService.IsStaff</c>; this measures it, because
    /// <c>ApiTokenAuthenticationHandler</c> stamping the <c>Admin</c> role claim is not something a
    /// reader of THIS file would think to check.
    /// </summary>
    [Fact]
    public async Task A_machine_token_opens_an_out_of_window_bundle()
    {
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.MenuRead]));

        var detail = await GetFromJsonAsync<ApiResponse<MenuBundleDto>>($"/api/Menus/{_lunchId}");

        detail!.Success.Should().BeTrue(detail.Message);
        detail.Data!.Name.Should().Be(LunchName);
    }

    private async Task<IReadOnlyList<string>> ListedNamesAsync()
    {
        var response = await GetFromJsonAsync<ApiResponse<PagedResult<MenuBundleDto>>>(
            "/api/Menus?page=1&pageSize=50");
        return response!.Data!.Items.Select(b => b.Name).ToList();
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        // Friday only, 22:00-02:00: a window that crosses midnight, which the replaced filter could
        // not match at any moment of any day.
        _lateNightId = await SeedBundleAsync(
            LateNightName, new TimeSpan(22, 0, 0), new TimeSpan(2, 0, 0), DayOfWeek.Friday);
        _lunchId = await SeedBundleAsync(
            LunchName, new TimeSpan(11, 0, 0), new TimeSpan(14, 0, 0), DayOfWeek.Friday);
        await SeedBundleAsync(AlwaysName, start: null, end: null, openOn: null);
    }

    private async Task<Guid> SeedBundleAsync(string name, TimeSpan? start, TimeSpan? end, DayOfWeek? openOn)
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
            MenuDefinition = new MenuDefinition
            {
                IsAlwaysAvailable = openOn is null,
                StartTime = start,
                EndTime = end,
                AvailableMonday = openOn == DayOfWeek.Monday,
                AvailableTuesday = openOn == DayOfWeek.Tuesday,
                AvailableWednesday = openOn == DayOfWeek.Wednesday,
                AvailableThursday = openOn == DayOfWeek.Thursday,
                AvailableFriday = openOn == DayOfWeek.Friday,
                AvailableSaturday = openOn == DayOfWeek.Saturday,
                AvailableSunday = openOn == DayOfWeek.Sunday,
                CreatedBy = "test"
            },
            CreatedBy = "test"
        };

        context.Products.Add(bundle);
        await context.SaveChangesAsync();

        return bundle.Id;
    }
}
