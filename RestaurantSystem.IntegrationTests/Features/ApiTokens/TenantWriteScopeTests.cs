using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Api.Features.Settings.Dtos;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;

namespace RestaurantSystem.IntegrationTests.Features.ApiTokens;

/// <summary>
/// API-TOKENS-PLAN §2 — the <c>tenant:write</c> scope: a machine client may fill in the
/// restaurant's own profile (address, phones, logo, opening hours).
/// </summary>
/// <remarks>
/// Every endpoint gets a PAIR of tests, and the pair is the point. A 403 on its own proves
/// nothing here: <c>ApiTokenScopeFilter</c> denies by default, so an UNANNOTATED endpoint answers
/// 403 to every token that will ever exist — the negative test would pass on an empty diff. Only
/// the positive half (a <c>tenant:write</c> token getting past the filter) proves the annotation
/// actually landed, and only the negative half (a <c>tenant:read</c> token still refused) proves
/// the scope discriminates rather than the endpoint being open to any token.
/// <para>
/// Lane 4 on purpose: these tests rewrite the RestaurantInfo singleton, which Respawn does NOT
/// reset (see <c>DatabaseFixture</c>'s ignore list). Lane 2 holds
/// <c>GetRestaurantInfoTests</c>, which asserts the migration's seeded address values; Lane 4
/// already contains <c>RestaurantInfoMutationTests</c> and expects that row to move.
/// </para>
/// </remarks>
[Collection("Database Lane 4")]
public class TenantWriteScopeTests : ApiTokenScopeTestBase
{
    private const string InfoUrl = "/api/restaurant-info";

    public TenantWriteScopeTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    // ── The restaurant profile ───────────────────────────────────────────

    [Fact]
    public async Task TenantWriteToken_CanUpdateTheProfile()
    {
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.TenantWrite]));

        var response = await PutAsJsonAsync(InfoUrl, ValidProfile("Kebab Dilhan"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await ReadResponseAsync<ApiResponse<RestaurantInfoDto>>(response);
        body!.Data!.Name.Should().Be("Kebab Dilhan");
    }

    [Fact]
    public async Task TenantReadToken_IsRefusedTheProfileUpdate()
    {
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.TenantRead]));

        var response = await PutAsJsonAsync(InfoUrl, ValidProfile("Should Not Land"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await ReadResponseAsync<ApiResponse<object>>(response);
        body!.ErrorCode.Should().Be(ErrorCodes.MissingScope);
    }

    // ── Phone numbers ────────────────────────────────────────────────────

    [Fact]
    public async Task TenantWriteToken_CanAddUpdateAndDeleteAPhoneNumber()
    {
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.TenantWrite]));

        var added = await PostAsJsonAsync($"{InfoUrl}/phones", new
        {
            label = "Machine",
            number = "+41227000900",
            whatsAppEnabled = false,
            displayOrder = 9,
            isActive = true
        });
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var id = (await ReadResponseAsync<ApiResponse<RestaurantPhoneNumberDto>>(added))!.Data!.Id;

        var updated = await PutAsJsonAsync($"{InfoUrl}/phones/{id}", new
        {
            id,
            label = "Machine 2",
            number = "+41227000901",
            whatsAppEnabled = true,
            displayOrder = 9,
            isActive = true
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

        var deleted = await Client.DeleteAsync($"{InfoUrl}/phones/{id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TenantReadToken_IsRefusedEveryPhoneWrite()
    {
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.TenantRead]));
        var someId = Guid.NewGuid();

        var post = await PostAsJsonAsync($"{InfoUrl}/phones", new { number = "+41227000902" });
        var put = await PutAsJsonAsync($"{InfoUrl}/phones/{someId}", new { number = "+41227000902" });
        var delete = await Client.DeleteAsync($"{InfoUrl}/phones/{someId}");

        post.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Logo ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TenantWriteToken_ReachesTheLogoUpload()
    {
        // An unknown variant, deliberately: the scope filter is an authorization filter and runs
        // BEFORE model binding, so a 400 from the enum bind is proof the request got past it —
        // without needing the stubbed file storage the real upload path requires
        // (see RestaurantLogoTests for why the shared host cannot write a file).
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.TenantWrite]));

        var response = await PutLogoAsync("sepia");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TenantReadToken_IsRefusedTheLogoUpload()
    {
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.TenantRead]));

        var response = await PutLogoAsync("light");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantWriteToken_CanDeleteTheLogo()
    {
        // Deleting a logo that was never set is a supported no-op (RestaurantLogoTests pins it),
        // so a 200 here is the scope check passing and nothing else.
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.TenantWrite]));

        var response = await Client.DeleteAsync($"{InfoUrl}/logo/dark");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TenantReadToken_IsRefusedTheLogoDelete()
    {
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.TenantRead]));

        var response = await Client.DeleteAsync($"{InfoUrl}/logo/dark");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Opening hours ────────────────────────────────────────────────────

    [Fact]
    public async Task TenantWriteToken_CanUpdateWorkingHours()
    {
        await SeedMondayHoursAsync();
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.TenantWrite]));

        var response = await PutAsJsonAsync("/api/WorkingHours", new UpdateWorkingHoursDto
        {
            DayOfWeek = DayOfWeek.Monday,
            OpenTime = new TimeSpan(11, 30, 0),
            CloseTime = new TimeSpan(22, 0, 0),
            IsActive = true,
            IsClosed = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await ReadResponseAsync<ApiResponse<WorkingHoursDto>>(response);
        body!.Success.Should().BeTrue();
        body.Data!.OpenTime.Should().Be(new TimeSpan(11, 30, 0));
    }

    [Fact]
    public async Task TenantReadToken_IsRefusedTheWorkingHoursUpdate()
    {
        await SeedMondayHoursAsync();
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.TenantRead]));

        var response = await PutAsJsonAsync("/api/WorkingHours", new UpdateWorkingHoursDto
        {
            DayOfWeek = DayOfWeek.Monday,
            OpenTime = new TimeSpan(3, 0, 0),
            CloseTime = new TimeSpan(4, 0, 0),
            IsActive = true,
            IsClosed = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── The boundary the scope deliberately does NOT cross ────────────────

    [Fact]
    public async Task TenantWriteToken_StillCannotChangeTaxConfiguration()
    {
        // API-TOKENS-PLAN §2: tenant:write is the restaurant's own profile, not what it charges.
        // TaxConfiguration carries no [ApiScope] at all, and absence is a denial.
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.TenantWrite]));

        var response = await PutAsJsonAsync("/api/TaxConfiguration", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static object ValidProfile(string name) => new
    {
        name,
        addressLine1 = "Rue de Carouge 12",
        addressLine2 = (string?)null,
        city = "Genève",
        postalCode = "1205",
        country = "Switzerland",
        latitude = (decimal?)null,
        longitude = (decimal?)null,
        email = "hello@example.test",
        website = (string?)null
    };

    private async Task<HttpResponseMessage> PutLogoAsync(string variant)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "logo", "logo.png");
        return await Client.PutAsync($"{InfoUrl}/logo/{variant}", content);
    }

    /// <summary>
    /// WorkingHours rows ARE wiped by the per-test Respawn reset, and
    /// <c>WorkingHoursService.UpdateAsync</c> updates an existing day rather than creating one —
    /// so without this the positive test would answer 404 and prove nothing about the scope.
    /// </summary>
    private async Task SeedMondayHoursAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (context.WorkingHours.Any(wh => wh.DayOfWeek == DayOfWeek.Monday))
        {
            return;
        }

        context.WorkingHours.Add(new WorkingHours
        {
            DayOfWeek = DayOfWeek.Monday,
            OpenTime = new TimeSpan(9, 0, 0),
            CloseTime = new TimeSpan(17, 0, 0),
            IsActive = true,
            IsClosed = false,
            CreatedBy = "test"
        });

        await context.SaveChangesAsync();
    }
}
