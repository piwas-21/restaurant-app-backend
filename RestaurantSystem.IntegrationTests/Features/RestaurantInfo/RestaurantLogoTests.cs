using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.DeleteRestaurantLogoCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantLogoCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace RestaurantSystem.IntegrationTests.Features.RestaurantInfo;

/// <summary>
/// SOFRA-ONBOARDING-PLAN O6 — the tenant's own logo on <c>/api/restaurant-info</c>.
/// </summary>
/// <remarks>
/// The behaviour worth pinning is not "an upload succeeds". It is that a tenant which has NOT
/// uploaded one reads back <c>null</c> — the clients render the restaurant's NAME as text off
/// exactly that, and before this feature every tenant image shipped with tenant-1's baked logo,
/// so a new restaurant's header showed another restaurant's brand. A regression that made the
/// field non-null (an empty string is the easy one) would silently restore that class of bug:
/// JavaScript's <c>??</c> does not fire on <c>""</c>, so the fallback would stop running and an
/// empty <c>src</c> would reach the header.
/// </remarks>
public class RestaurantLogoTests : IntegrationTestBase
{
    private const string Url = "/api/restaurant-info";

    public RestaurantLogoTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    // ── The default state ────────────────────────────────────────────────

    [Fact]
    public async Task AFreshTenantReadsBackNoLogoAtAll()
    {
        var info = await GetInfoAsync();

        // Null, not "" — see the class remark.
        info.LogoUrl.Should().BeNull();
        info.LogoDarkUrl.Should().BeNull();
    }

    // ── Authorization ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateLogo_NonAdminCaller_Returns403()
    {
        var response = await PutLogoAsync(LogoVariant.Light, PngFile());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteLogo_NonAdminCaller_Returns403()
    {
        var response = await Client.DeleteAsync($"{Url}/logo/light");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Routing ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnknownVariantIsRejectedBeforeAnythingIsStored()
    {
        // The variant is bound from the route, so a typo has to fail loudly rather than
        // defaulting to Light and overwriting the logo the caller did not mean to touch.
        AuthenticateAsAdmin();

        var response = await PutLogoAsync("sepia", PngFile());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetInfoAsync()).LogoUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("LIGHT")]
    public async Task BothVariantsRoundTripThroughTheApi(string variant)
    {
        using var host = StubbedStorageHost();

        var response = await PutLogoAsync(host.Client, variant, PngFile());

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var info = await GetInfoAsync(host.Client);
        var stored = variant.Equals("dark", StringComparison.OrdinalIgnoreCase)
            ? info.LogoDarkUrl
            : info.LogoUrl;
        stored.Should().Be(host.Storage.Uploads.Single());
    }

    [Fact]
    public async Task UploadingTheLightLogoLeavesTheDarkOneAlone()
    {
        // The two are independent fields behind one route shape; writing through the wrong
        // one is the mistake the LogoVariant switch exists to prevent, and it is invisible
        // until someone with both logos notices one replaced the other.
        using var host = StubbedStorageHost();
        (await PutLogoAsync(host.Client, LogoVariant.Dark, PngFile())).EnsureSuccessStatusCode();
        var darkBefore = (await GetInfoAsync(host.Client)).LogoDarkUrl;
        darkBefore.Should().NotBeNullOrEmpty();

        (await PutLogoAsync(host.Client, LogoVariant.Light, PngFile())).EnsureSuccessStatusCode();

        var info = await GetInfoAsync(host.Client);
        info.LogoUrl.Should().NotBeNullOrEmpty().And.NotBe(darkBefore);
        info.LogoDarkUrl.Should().Be(darkBefore);
    }

    // ── Delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingTheLogoReturnsTheTenantToTheNameOnlyDefault()
    {
        using var host = StubbedStorageHost();
        (await PutLogoAsync(host.Client, LogoVariant.Light, PngFile())).EnsureSuccessStatusCode();

        var response = await host.Client.DeleteAsync($"{Url}/logo/light");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetInfoAsync(host.Client)).LogoUrl
            .Should().BeNull("removing a logo is a supported end state, not an error");
    }

    [Fact]
    public async Task DeletingALogoThatWasNeverSetIsANoOp()
    {
        AuthenticateAsAdmin();

        var response = await Client.DeleteAsync($"{Url}/logo/dark");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetInfoAsync()).LogoDarkUrl.Should().BeNull();
    }

    // ── Validation (handler level, with a stub store) ─────────────────────

    [Theory]
    [InlineData("evil.exe", "image/png", "*File type not allowed*")]
    [InlineData("logo.png", "text/html", "*MIME type*")]
    public async Task RejectsAFileThatFailsTheUploadAllowlist(string name, string contentType, string expected)
    {
        var result = await HandleUploadAsync(LogoVariant.Light, File(name, contentType, [1, 2, 3]));

        result.Success.Should().BeFalse();
        // The reason lives in `errors[0]`; `ApiResponse.Failure(error)` sets message to a
        // generic "Operation failed", so asserting on Message would pass for the WRONG rejection.
        result.Errors.Should().ContainMatch(expected);
    }

    [Fact]
    public async Task RejectsAnEmptyFile()
    {
        var result = await HandleUploadAsync(LogoVariant.Light, File("logo.png", "image/png", []));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainMatch("*No image file provided*");
    }

    [Fact]
    public async Task ARejectedUploadStoresNothing()
    {
        // A rejection that had already written the URL would leave the row pointing at a file
        // that was never uploaded — a broken image in the header, from a request that failed.
        var storage = new StubStorage();

        var result = await HandleUploadAsync(LogoVariant.Light, File("evil.exe", "image/png", [1]), storage);

        result.Success.Should().BeFalse();
        storage.Uploads.Should().BeEmpty();
        (await GetInfoAsync()).LogoUrl.Should().BeNull();
    }

    // ── Storage housekeeping ─────────────────────────────────────────────

    [Fact]
    public async Task ReplacingALogoDeletesTheFileItReplaced()
    {
        // Without this the uploads volume grows by one file per edit, on a bind-mount that
        // is also what the nightly backups carry.
        var storage = new StubStorage();
        (await HandleUploadAsync(LogoVariant.Light, PngFile(), storage)).Success.Should().BeTrue();
        var first = storage.Uploads.Single();

        (await HandleUploadAsync(LogoVariant.Light, PngFile(), storage)).Success.Should().BeTrue();

        storage.Deleted.Should().ContainSingle().Which.Should().Be(first);
    }

    [Fact]
    public async Task TheFirstUploadDeletesNothing()
    {
        var storage = new StubStorage();

        (await HandleUploadAsync(LogoVariant.Light, PngFile(), storage)).Success.Should().BeTrue();

        storage.Deleted.Should().BeEmpty("there was no previous file to remove");
    }

    [Fact]
    public async Task DeletingTheLogoAlsoRemovesTheStoredFile()
    {
        var storage = new StubStorage();
        (await HandleUploadAsync(LogoVariant.Dark, PngFile(), storage)).Success.Should().BeTrue();
        var uploaded = storage.Uploads.Single();

        using var scope = Factory.Services.CreateScope();
        var handler = new DeleteRestaurantLogoCommandHandler(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            storage,
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            scope.ServiceProvider.GetRequiredService<IConfiguration>());

        var result = await handler.Handle(new DeleteRestaurantLogoCommand(LogoVariant.Dark), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.LogoDarkUrl.Should().BeNull();
        storage.Deleted.Should().ContainSingle().Which.Should().Be(uploaded);
    }

    // ── Audit ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUploadStampsTheSingletonAsModified()
    {
        // RestaurantInfoSeeder treats `UpdatedAt == null` as "pristine, safe to overwrite".
        // A logo upload that did not stamp it would leave the row eligible for the seeder to
        // reset the restaurant's name and address on the next boot.
        using var host = StubbedStorageHost();

        (await PutLogoAsync(host.Client, LogoVariant.Light, PngFile())).EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await context.RestaurantInfo.AsNoTracking().FirstAsync();
        row.UpdatedAt.Should().NotBeNull();
        row.UpdatedBy.Should().NotBeNullOrEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Clears the two logo columns before every test in this class.
    /// </summary>
    /// <remarks>
    /// <c>RestaurantInfo</c> is on <c>DatabaseFixture</c>'s Respawn ignore list — it is a
    /// migration-seeded singleton, so wiping it between tests would leave nothing to read.
    /// That means a logo written by one test survives into the next, and
    /// <c>AFreshTenantReadsBackNoLogoAtAll</c> would assert the default state against a row
    /// some earlier test had already branded. Every "no logo" assertion here depends on this.
    /// </remarks>
    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.RestaurantInfo.ExecuteUpdateAsync(s => s
            .SetProperty(r => r.LogoUrl, (string?)null)
            .SetProperty(r => r.LogoDarkUrl, (string?)null)
            .SetProperty(r => r.UpdatedAt, (DateTime?)null)
            .SetProperty(r => r.UpdatedBy, (string?)null));
    }

    private Task<RestaurantInfoDto> GetInfoAsync() => GetInfoAsync(Client);

    private static async Task<RestaurantInfoDto> GetInfoAsync(HttpClient client)
    {
        var response = await client.GetAsync(Url);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<RestaurantInfoDto>>(JsonOptions);
        return payload!.Data!;
    }

    private Task<HttpResponseMessage> PutLogoAsync(LogoVariant variant, FormFile file) =>
        PutLogoAsync(Client, variant.ToString().ToLowerInvariant(), file);

    private Task<HttpResponseMessage> PutLogoAsync(string variant, FormFile file) =>
        PutLogoAsync(Client, variant, file);

    private static Task<HttpResponseMessage> PutLogoAsync(HttpClient client, LogoVariant variant, FormFile file) =>
        PutLogoAsync(client, variant.ToString().ToLowerInvariant(), file);

    private static async Task<HttpResponseMessage> PutLogoAsync(HttpClient client, string variant, FormFile file)
    {
        using var content = new MultipartFormDataContent();
        var stream = new StreamContent(file.OpenReadStream());
        stream.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        content.Add(stream, "logo", file.FileName);
        return await client.PutAsync($"{Url}/logo/{variant}", content);
    }

    /// <summary>
    /// An admin-authenticated client whose <see cref="IFileStorageService"/> is the recording
    /// stub, for the tests that have to go over the real route.
    /// </summary>
    /// <remarks>
    /// The shared test host cannot store a file: <c>LocalFileStorageService</c> reads
    /// <c>LocalStorage:BaseUrl</c>, which no test configuration defines, so a real upload throws
    /// and the handler answers "Failed to upload logo" with a 200 envelope. (That is why the
    /// sibling <c>UpdateCategoryImageCommandTests</c> only ever calls its handler directly, and
    /// why <c>EnsureSuccessStatusCode</c> is not enough on its own here — the failure arrives as
    /// <c>success: false</c> inside a 200.) Swapping the service keeps the controller, routing,
    /// model binding and authorization in the test while taking the disk out of it.
    /// </remarks>
    private StubbedStorageClient StubbedStorageHost()
    {
        var storage = new StubStorage();
        var factory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFileStorageService>();
                services.AddSingleton<IFileStorageService>(storage);
            }));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        return new StubbedStorageClient(factory, client, storage);
    }

    private sealed record StubbedStorageClient(
        WebApplicationFactory<Program> Factory, HttpClient Client, StubStorage Storage) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }

    private async Task<ApiResponse<RestaurantInfoDto>> HandleUploadAsync(
        LogoVariant variant, FormFile file, StubStorage? storage = null)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = new UpdateRestaurantLogoCommandHandler(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            storage ?? new StubStorage(),
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            NullLogger<UpdateRestaurantLogoCommandHandler>.Instance,
            scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            scope.ServiceProvider.GetRequiredService<IOptions<FileStorageSettings>>());

        return await handler.Handle(new UpdateRestaurantLogoCommand(variant, file), CancellationToken.None);
    }

    private static FormFile PngFile() => File("logo.png", "image/png", [0x89, 0x50, 0x4E, 0x47]);

    private static FormFile File(string name, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "logo", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    /// <summary>
    /// Records what it was asked to store and remove, so the housekeeping assertions can be
    /// made on OBSERVED calls rather than on the handler's return value.
    /// </summary>
    private sealed class StubStorage : IFileStorageService
    {
        private int _counter;
        private readonly Lock _gate = new();

        public List<string> Uploads { get; } = [];
        public List<string> Deleted { get; } = [];

        public Task<string> UploadFileAsync(IFormFile file, string folder, string? fileName = null, CancellationToken cancellationToken = default)
        {
            // A distinct URL per call: a stub returning a constant would make the
            // "replacing deletes the old file" assertion pass even if the handler deleted
            // the file it had just written.
            var url = $"https://example.test/{folder}/{Interlocked.Increment(ref _counter)}.png";
            lock (_gate)
            {
                Uploads.Add(url);
            }
            return Task.FromResult(url);
        }

        public Task<string> UploadFileAsync(Stream stream, string folder, string fileName, string contentType, CancellationToken cancellationToken = default)
            => UploadFileAsync(new FormFile(stream, 0, stream.Length, "logo", fileName), folder, fileName, cancellationToken);

        public Task<bool> DeleteFileAsync(string fileUrl, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Deleted.Add(fileUrl);
            }
            return Task.FromResult(true);
        }

        public Task<string> GetSignedUrlAsync(string fileKey, TimeSpan expirationTime, CancellationToken cancellationToken = default) => Task.FromResult(fileKey);
        public Task<bool> FileExistsAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<FileMetadata?> GetFileMetadataAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult<FileMetadata?>(null);
    }
}
