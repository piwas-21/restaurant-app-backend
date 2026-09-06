using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Products.Commands.UploadProductImageCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// Partner-reported menu slowness (2026-09-06): the card variant is generated AT UPLOAD, while the
/// bytes are in hand, and recorded on the row so the guest card can serve ~100 KB of WebP instead
/// of the multi-hundred-KB original. Fail-open contract: a variant that cannot be produced must
/// never fail the upload — the guest then gets the original, which is the pre-feature behaviour.
/// </summary>
/// <remarks>
/// Drives the handler DIRECTLY with a recording stub storage: the test host's real storage
/// provider is S3, and the variant contract under test — generate, name as
/// <c>&lt;stem&gt;-800.webp</c>, store beside the original, record the URL — is storage-agnostic.
/// </remarks>
[Collection("Database Lane 1")]
public class ProductCardVariantUploadTests : IntegrationTestBase
{
    public ProductCardVariantUploadTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    private const string Actor = "card-variant-test";

    private static readonly Guid ProductId = Guid.NewGuid();

    // A 1x1 PNG: decodable by ImageSharp, no dimensions the decode guard could refuse.
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Products.Add(new Product
        {
            Id = ProductId,
            Name = "CV Test Dish",
            BasePrice = 4m,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.MainItem,
            AvailableOrderTypes = null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor,
        });
        await context.SaveChangesAsync();
    }

    private static FormFile TinyPngUpload()
    {
        var bytes = Convert.FromBase64String(TinyPngBase64);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "card-variant-test.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png",
        };
    }

    [Fact]
    public async Task An_upload_records_a_card_variant_the_guest_can_serve()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IImageProcessor>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var fileStorageSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileStorageSettings>>();

        var storage = new RecordingStorage();
        var handler = new UploadProductImageCommandHandler(
            context,
            storage,
            processor,
            currentUser,
            NullLogger<UploadProductImageCommandHandler>.Instance,
            configuration,
            fileStorageSettings);

        var response = await handler.Handle(new UploadProductImageCommand
        {
            ProductId = ProductId,
            Image = TinyPngUpload(),
            IsPrimary = true,
        }, CancellationToken.None);

        response.Success.Should().BeTrue("positive control: the upload itself must succeed");
        response.Data!.CardUrl.Should().NotBeNullOrEmpty("the card variant is the whole point");
        response.Data.CardUrl.Should().EndWith("-800.webp");

        storage.LastStreamUpload.Should().NotBeNull();
        var upload = storage.LastStreamUpload!;
        upload.Folder.Should().Be($"products/{ProductId}", "the variant lives beside the original");
        upload.FileName.Should().EndWith("-800.webp");
        upload.ContentType.Should().Be("image/webp");

        // A real WebP container, not an error page or a truncated write: RIFF box + WEBP mark.
        var bytes = upload.Bytes;
        bytes.Length.Should().BeGreaterThan(12);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("RIFF");
        System.Text.Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WEBP");

        // And the row records it, because the map is what the guest query reads.
        var row = await context.ProductImages.AsNoTracking().SingleAsync(i => i.ProductId == ProductId);
        row.CardUrl.Should().NotBeNullOrEmpty();
    }

    private sealed record StreamUpload(string Folder, string FileName, string ContentType, byte[] Bytes);

    private sealed class RecordingStorage : IFileStorageService
    {
        public StreamUpload? LastStreamUpload { get; private set; }

        public Task<string> UploadFileAsync(IFormFile file, string folder, string? fileName = null, CancellationToken cancellationToken = default) =>
            Task.FromResult($"https://example.test/{folder}/{file.FileName}");

        public async Task<string> UploadFileAsync(Stream stream, string folder, string fileName, string contentType, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            LastStreamUpload = new StreamUpload(folder, fileName, contentType, buffer.ToArray());
            return $"https://example.test/{folder}/{fileName}";
        }

        public Task<bool> DeleteFileAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string> GetSignedUrlAsync(string fileKey, TimeSpan expirationTime, CancellationToken cancellationToken = default) => Task.FromResult(fileKey);
        public Task<bool> FileExistsAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<FileMetadata?> GetFileMetadataAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult<FileMetadata?>(null);
    }
}
