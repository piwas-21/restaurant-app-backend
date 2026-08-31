using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.RestaurantInfo;

/// <summary>
/// The public landing-page contract: <c>GET /api/restaurant-info/landing</c> (anonymous) and
/// <c>PUT /api/restaurant-info/landing</c> (admin).
///
/// <para>
/// The endpoint is deliberately SEPARATE from the restaurant profile: the profile PUT is full
/// replace and its validator requires the address block, while the landing page needs mode +
/// copy only, read anonymously by every tenant frontend. Copy rows are per language and FULL
/// REPLACE too — an omitted locale row is removed, and blank copy is stored as null so the
/// client falls back to its bundled translation instead of rendering an empty heading.
/// </para>
///
/// <para>
/// Lane 4 on purpose: a PUT rewrites the RestaurantInfo singleton (background mode), which
/// Respawn does not reset — the same constraint that put the profile and interior-image tests
/// here.
/// </para>
/// </summary>
[Collection("Database Lane 4")]
public class RestaurantLandingPageTests : IntegrationTestBase
{
    private const string Url = "/api/restaurant-info/landing";

    public RestaurantLandingPageTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    // ── The default state ────────────────────────────────────────────────

    [Fact]
    public async Task AFreshTenantReadsTheDefaultModeAndNoOverridesAnonymously()
    {
        // RestaurantInfo survives the per-test Respawn wipe (singleton ignore list), so the
        // pristine state is ARRANGED, never assumed from test order.
        await SeedLandingStateAsync(Domain.Entities.LandingBackgroundMode.Default, null);

        var landing = await GetLandingAsync();

        landing.BackgroundMode.Should().Be("default");
        landing.BackgroundImageUrl.Should().BeNull("no upload exists, so the platform artwork shows");
        landing.Content.Should().BeEmpty("no copy overrides have been written");
    }

    [Fact]
    public async Task CustomMode_WithAnUpload_ServesTheUploadAsTheBackground()
    {
        // Seeded directly: the admin arrives in this state by uploading once and choosing
        // "my own photo" — the migration seeds `Custom` for tenants whose upload predates the
        // mode column, and this is the read-back contract those tenants rely on.
        await SeedLandingStateAsync(Domain.Entities.LandingBackgroundMode.Custom, "room.webp");

        var landing = await GetLandingAsync();

        landing.BackgroundMode.Should().Be("custom");
        landing.BackgroundImageUrl.Should().NotBeNullOrEmpty().And.Contain("room.webp");
    }

    // ── Authorization ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateLanding_NonAdminCaller_Returns403()
    {
        var response = await Client.PutAsJsonAsync(Url, ValidBody("none"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── The round trip ───────────────────────────────────────────────────

    [Fact]
    public async Task TheAdminCanWriteCopyForTwoLanguages_AndReadItBackAnonymously()
    {
        AuthenticateAsAdmin();
        var response = await Client.PutAsJsonAsync(Url, ValidBody(
            "none",
            Row(languageCode: "en", welcomeTitle: "Welcome to Dilhan", storyBody: "Since 1998."),
            Row(languageCode: "tr", welcomeTitle: "Dilhan'a hoş geldiniz", storyBody: "   ")));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var landing = await GetLandingAsync();
        landing.BackgroundMode.Should().Be("none");
        landing.Content.Should().ContainKeys("en", "tr");
        landing.Content["en"].WelcomeTitle.Should().Be("Welcome to Dilhan");
        landing.Content["tr"].StoryBody.Should()
            .BeNull("a BLANK string is stored as null so the client falls back to its bundled translation");
    }

    [Fact]
    public async Task AnOmittedLocaleRowIsRemoved_ByTheFullReplacePut()
    {
        AuthenticateAsAdmin();
        (await Client.PutAsJsonAsync(Url, ValidBody("none",
            Row("en", welcomeTitle: "English title"),
            Row("de", welcomeTitle: "Deutscher Titel")))).EnsureSuccessStatusCode();

        (await Client.PutAsJsonAsync(Url, ValidBody("none", Row("en", welcomeTitle: "English title"))))
            .EnsureSuccessStatusCode();

        var landing = await GetLandingAsync();
        landing.Content.Should().ContainKey("en");
        landing.Content.Should().NotContainKey("de", "a full replace removes locales the admin no longer supplies");
    }

    // ── The custom mode is guarded ───────────────────────────────────────

    [Fact]
    public async Task CustomMode_WithoutAnUpload_IsRefused()
    {
        // The refusal needs the no-upload precondition — the row survives Respawn, so an
        // earlier test's upload must not turn this 400 into a 200.
        await SeedLandingStateAsync(Domain.Entities.LandingBackgroundMode.Default, null);
        AuthenticateAsAdmin();

        var response = await Client.PutAsJsonAsync(Url, ValidBody("custom"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("interior image", "the refusal must say WHAT is missing");
    }

    // ── Validation ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnknownModeAndAnUnknownLanguageAreRefused()
    {
        AuthenticateAsAdmin();

        var badMode = await Client.PutAsJsonAsync(Url, ValidBody("neon"));
        badMode.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badLanguage = await Client.PutAsJsonAsync(Url, ValidBody("none", Row("xx", welcomeTitle: "…")));
        badLanguage.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task SeedLandingStateAsync(Domain.Entities.LandingBackgroundMode mode, string? interiorImageUrl)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var info = await context.RestaurantInfo.SingleAsync();
        info.LandingBackgroundMode = mode;
        info.InteriorImageUrl = interiorImageUrl;
        await context.SaveChangesAsync();
    }

    private sealed record LandingContentRowShorthand(string? LanguageCode, string? WelcomeTitle, string? StoryBody);

    private static LandingContentRowShorthand Row(string languageCode, string? welcomeTitle = null, string? storyBody = null) =>
        new LandingContentRowShorthand(languageCode, welcomeTitle, storyBody);

    private static object ValidBody(string mode, params LandingContentRowShorthand[] rows) =>
        new { backgroundMode = mode, content = rows };

    private async Task<LandingPageDtoShape> GetLandingAsync()
    {
        var response = await Client.GetAsync(Url);
        response.EnsureSuccessStatusCode();
        var body = await ReadResponseAsync<ApiResponse<LandingPageDtoShape>>(response);
        body!.Data.Should().NotBeNull();
        return body.Data!;
    }

    private sealed record LandingPageDtoShape(
        string BackgroundMode,
        string? BackgroundImageUrl,
        IReadOnlyDictionary<string, LandingContentDtoShape> Content);

    private sealed record LandingContentDtoShape(
        string? HeroEyebrow,
        string? WelcomeTitle,
        string? WelcomeBody,
        string? StoryTitle,
        string? StoryBody);
}
