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
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.DeleteRestaurantInteriorImageCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantInfoCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Commands.UpdateRestaurantInteriorImageCommand;
using RestaurantSystem.Api.Features.RestaurantInfo.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace RestaurantSystem.IntegrationTests.Features.RestaurantInfo;

/// <summary>
/// The tenant's own interior photo on <c>/api/restaurant-info</c> — the landing-page section a
/// restaurant fills in for itself.
/// </summary>
/// <remarks>
/// Two behaviours carry this feature, and neither is "an upload succeeds".
/// <para>
/// First, a tenant that has NOT uploaded one reads back <c>null</c> rather than <c>""</c>. The
/// clients render the section as <c>interiorImageUrl &amp;&amp; &lt;section&gt;</c>, and there is
/// no acceptable stand-in: <c>/branding/hero.png</c> is a neutral platform graphic that belongs
/// to no restaurant, so a non-null empty string would put a heading like "our restaurant" above
/// a broken image on every tenant that never uploaded a photo.
/// </para>
/// <para>
/// Second — <see cref="AProfilePutThatKnowsNothingOfThePhotoLeavesItAlone"/> — the profile PUT is
/// FULL REPLACE and this field is not part of it. The photo is owned exclusively by its own
/// upload/delete endpoints, exactly as the two logo URLs are. That is what stops an admin saving
/// their address, or a machine client written before this field existed, from silently deleting
/// the restaurant's photo.
/// </para>
/// <para>
/// Lane 4 on purpose: the profile-PUT test rewrites the RestaurantInfo singleton, which Respawn
/// does NOT reset (see <c>DatabaseFixture</c>'s ignore list). Lane 2 holds
/// <c>GetRestaurantInfoTests</c>, which asserts the migration's seeded address values and would
/// break; Lane 4 already contains <c>RestaurantInfoMutationTests</c> and expects that row to move.
/// </para>
/// </remarks>
[Collection("Database Lane 4")]
public class RestaurantInteriorImageTests : IntegrationTestBase
{
    private const string Url = "/api/restaurant-info";
    private const string ImageUrl = $"{Url}/interior-image";

    public RestaurantInteriorImageTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    // ── The default state ────────────────────────────────────────────────

    [Fact]
    public async Task AFreshTenantReadsBackNoInteriorPhotoAtAll()
    {
        var info = await GetInfoAsync();

        // Null, not "" — see the class remark.
        info.InteriorImageUrl.Should().BeNull();
    }

    // ── Authorization ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateInteriorImage_NonAdminCaller_Returns403()
    {
        var response = await PutImageAsync(Client, PngFile());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteInteriorImage_NonAdminCaller_Returns403()
    {
        var response = await Client.DeleteAsync(ImageUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── The round trip ───────────────────────────────────────────────────

    [Fact]
    public async Task TheUploadedPhotoIsReadableFromThePublicEndpoint()
    {
        // Public, because the landing page reads it anonymously — an upload that only the
        // admin could read back would render nothing for the visitors it exists for.
        using var host = StubbedStorageHost();

        var response = await PutImageAsync(host.Client, PngFile());

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await GetInfoAsync(host.Client)).InteriorImageUrl.Should().Be(host.Storage.Uploads.Single());
    }

    // ── The full-replace PUT ─────────────────────────────────────────────

    [Fact]
    public async Task AProfilePutThatKnowsNothingOfThePhotoLeavesItAlone()
    {
        // UpdateRestaurantInfoCommand assigns every one of its fields unconditionally, so any
        // field it CARRIES is cleared by an omitted value. This one is deliberately not on that
        // command at all — the check is that the address save cannot reach the photo.
        using var host = StubbedStorageHost();
        (await PutImageAsync(host.Client, PngFile())).EnsureSuccessStatusCode();
        var uploaded = (await GetInfoAsync(host.Client)).InteriorImageUrl;
        uploaded.Should().NotBeNullOrEmpty();

        AuthenticateAsAdmin();
        var response = await Client.PutAsJsonAsync(Url, new UpdateRestaurantInfoCommand(
            Name: "Kebab Dilhan",
            AddressLine1: "Rue de Carouge 12",
            AddressLine2: null,
            City: "Genève",
            PostalCode: "1205",
            Country: "Switzerland",
            Latitude: null,
            Longitude: null,
            Email: "hello@example.test",
            Website: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        // Asserted on the PUT's OWN response body as well as on a re-read: a handler that cleared
        // the column would still answer a stale DTO if the assertion only re-read a cached row.
        var returned = await ReadResponseAsync<ApiResponse<RestaurantInfoDto>>(response);
        returned!.Data!.InteriorImageUrl.Should().Be(uploaded);
        (await GetInfoAsync()).InteriorImageUrl.Should().Be(uploaded);
    }

    // ── Delete ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingThePhotoReturnsTheTenantToTheNoSectionDefault()
    {
        using var host = StubbedStorageHost();
        (await PutImageAsync(host.Client, PngFile())).EnsureSuccessStatusCode();

        var response = await host.Client.DeleteAsync(ImageUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetInfoAsync(host.Client)).InteriorImageUrl
            .Should().BeNull("removing the photo is a supported end state, not an error");
    }

    [Fact]
    public async Task DeletingAPhotoThatWasNeverSetIsANoOp()
    {
        AuthenticateAsAdmin();

        var response = await Client.DeleteAsync(ImageUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetInfoAsync()).InteriorImageUrl.Should().BeNull();
    }

    // ── Validation (handler level, with a stub store) ─────────────────────

    [Theory]
    [InlineData("evil.exe", "image/png", "*File type not allowed*")]
    [InlineData("interior.png", "text/html", "*MIME type*")]
    public async Task RejectsAFileThatFailsTheUploadAllowlist(string name, string contentType, string expected)
    {
        var result = await HandleUploadAsync(File(name, contentType, [1, 2, 3]));

        result.Success.Should().BeFalse();
        // The reason lives in `errors[0]`; `ApiResponse.Failure(error)` sets message to a
        // generic "Operation failed", so asserting on Message would pass for the WRONG rejection.
        result.Errors.Should().ContainMatch(expected);
    }

    [Fact]
    public async Task ARejectedUploadStoresNothing()
    {
        // A rejection that had already written the URL would leave the row pointing at a file
        // that was never uploaded — a broken image on the landing page, from a failed request.
        var storage = new StubStorage();

        var result = await HandleUploadAsync(File("evil.exe", "image/png", [1]), storage);

        result.Success.Should().BeFalse();
        storage.Uploads.Should().BeEmpty();
        (await GetInfoAsync()).InteriorImageUrl.Should().BeNull();
    }

    // ── Storage housekeeping ─────────────────────────────────────────────

    [Fact]
    public async Task ReplacingThePhotoDeletesTheFileItReplaced()
    {
        // Without this the uploads volume grows by one file per edit, on a bind-mount that
        // is also what the nightly backups carry.
        var storage = new StubStorage();
        (await HandleUploadAsync(PngFile(), storage)).Success.Should().BeTrue();
        var first = storage.Uploads.Single();

        (await HandleUploadAsync(PngFile(), storage)).Success.Should().BeTrue();

        storage.Deleted.Should().ContainSingle().Which.Should().Be(first);
    }

    [Fact]
    public async Task TheFirstUploadDeletesNothing()
    {
        var storage = new StubStorage();

        (await HandleUploadAsync(PngFile(), storage)).Success.Should().BeTrue();

        storage.Deleted.Should().BeEmpty("there was no previous file to remove");
    }

    [Fact]
    public async Task DeletingThePhotoAlsoRemovesTheStoredFile()
    {
        var storage = new StubStorage();
        (await HandleUploadAsync(PngFile(), storage)).Success.Should().BeTrue();
        var uploaded = storage.Uploads.Single();

        using var scope = Factory.Services.CreateScope();
        var handler = new DeleteRestaurantInteriorImageCommandHandler(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            storage,
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            scope.ServiceProvider.GetRequiredService<IConfiguration>());

        var result = await handler.Handle(
            new DeleteRestaurantInteriorImageCommand(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.InteriorImageUrl.Should().BeNull();
        storage.Deleted.Should().ContainSingle().Which.Should().Be(uploaded);
    }

    // ── Audit ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUploadStampsTheSingletonAsModified()
    {
        // RestaurantInfoSeeder treats `UpdatedAt == null` as "pristine, safe to overwrite".
        // An upload that did not stamp it would leave the row eligible for the seeder to
        // reset the restaurant's name and address on the next boot.
        using var host = StubbedStorageHost();

        (await PutImageAsync(host.Client, PngFile())).EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await context.RestaurantInfo.AsNoTracking().FirstAsync();
        row.UpdatedAt.Should().NotBeNull();
        row.UpdatedBy.Should().NotBeNullOrEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Clears the interior-photo column before every test in this class.
    /// </summary>
    /// <remarks>
    /// <c>RestaurantInfo</c> is on <c>DatabaseFixture</c>'s Respawn ignore list — it is a
    /// migration-seeded singleton, so wiping it between tests would leave nothing to read. That
    /// means a photo written by one test survives into the next, and
    /// <c>AFreshTenantReadsBackNoInteriorPhotoAtAll</c> would assert the default state against a
    /// row an earlier test had already filled in.
    /// </remarks>
    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.RestaurantInfo.ExecuteUpdateAsync(s => s
            .SetProperty(r => r.InteriorImageUrl, (string?)null)
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

    private static async Task<HttpResponseMessage> PutImageAsync(HttpClient client, FormFile file)
    {
        using var content = new MultipartFormDataContent();
        var stream = new StreamContent(file.OpenReadStream());
        stream.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        content.Add(stream, "image", file.FileName);
        return await client.PutAsync(ImageUrl, content);
    }

    /// <summary>
    /// An admin-authenticated client whose <see cref="IFileStorageService"/> is the recording
    /// stub, for the tests that have to go over the real route.
    /// </summary>
    /// <remarks>
    /// The shared test host cannot store a file: <c>LocalFileStorageService</c> reads
    /// <c>LocalStorage:BaseUrl</c>, which no test configuration defines, so a real upload throws
    /// and the handler answers "Failed to upload interior image" inside a 200 envelope — which is
    /// why <c>EnsureSuccessStatusCode</c> is not enough on its own here. Swapping the service
    /// keeps the controller, routing, model binding and authorization in the test while taking
    /// the disk out of it. Same reasoning as <c>RestaurantLogoTests</c>.
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
        FormFile file, StubStorage? storage = null)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = new UpdateRestaurantInteriorImageCommandHandler(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            storage ?? new StubStorage(),
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            NullLogger<UpdateRestaurantInteriorImageCommandHandler>.Instance,
            scope.ServiceProvider.GetRequiredService<IConfiguration>(),
            scope.ServiceProvider.GetRequiredService<IOptions<FileStorageSettings>>());

        return await handler.Handle(
            new UpdateRestaurantInteriorImageCommand(file), CancellationToken.None);
    }

    private static FormFile PngFile() => File("interior.png", "image/png", [0x89, 0x50, 0x4E, 0x47]);

    private static FormFile File(string name, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "image", name)
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
            => UploadFileAsync(new FormFile(stream, 0, stream.Length, "image", fileName), folder, fileName, cancellationToken);

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
