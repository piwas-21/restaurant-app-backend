using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Products.Commands.UploadMultipleProductImagesCommand;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// Track F1b — the bulk product-image upload used to answer a total failure with
/// <c>SuccessWithData([])</c> and <c>errors: null</c>, so RUMI's photo uploads looked like a no-op
/// for weeks while the real reason ("File 'blob' has invalid extension") sat in the server log.
/// </summary>
/// <remarks>
/// These tests pin the three-way response contract — all-fail, partial, all-succeed — because it is
/// the contract, not the validation, that was broken: the per-file reasons were already computed.
/// A stub <see cref="IFileStorageService"/> keeps the subject the handler's own bookkeeping rather
/// than S3.
/// </remarks>
[Collection("Database Lane 1")]
public class UploadMultipleProductImagesCommandTests : IntegrationTestBase
{
    public UploadMultipleProductImagesCommandTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public async Task WhenEveryFileIsRejected_ItFailsAndSaysWhyForEachOne()
    {
        var result = await HandleAsync(ProductId, File("evil.exe", "image/png", [1, 2, 3]), File("note.txt", "text/plain", [1]));

        result.Success.Should().BeFalse("a batch that stored nothing is not a success");
        result.Data.Should().BeNullOrEmpty();
        result.Errors.Should().HaveCount(2);
        result.Errors.Should().ContainMatch("*evil.exe*").And.ContainMatch("*note.txt*");
    }

    [Fact]
    public async Task TheProductionCase_AFileNamedBlob_IsNamedInTheError()
    {
        // The literal shape of the reported bug: browser-image-compression returns a Blob, so the
        // multipart part arrives with filename "blob" and no extension. Whatever the frontend fix,
        // the server must be able to say this out loud.
        var result = await HandleAsync(ProductId, File("blob", "image/jpeg", [1, 2, 3]));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainMatch("*'blob'*");
    }

    [Fact]
    public async Task WhenSomeFilesAreRejected_ItSucceedsAndStillReportsTheRejectedOnes()
    {
        // Partial success stays a success envelope — the stored images ARE saved and the client
        // must render them — but it carries the reasons so the user learns which photo is missing.
        var result = await HandleAsync(ProductId, PngFile("good.png"), File("evil.exe", "image/png", [1, 2, 3]));

        result.Success.Should().BeTrue(result.Message);
        result.Data.Should().HaveCount(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("evil.exe");
    }

    [Fact]
    public async Task WhenEveryFileIsStored_ThereAreNoErrors()
    {
        var result = await HandleAsync(ProductId, PngFile("one.png"), PngFile("two.png"));

        result.Success.Should().BeTrue(result.Message);
        result.Data.Should().HaveCount(2);
        result.Errors.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ARejectedFirstFileNoLongerCostsTheBatchItsPrimaryImage()
    {
        // Was `IsPrimary = !hasPrimaryImage && i == 0`, indexed on the REQUEST rather than on what
        // was actually stored: reject file 0 and the product ended up with images and no primary.
        var result = await HandleAsync(ProductId, File("evil.exe", "image/png", [1, 2, 3]), PngFile("good.png"));

        result.Success.Should().BeTrue(result.Message);
        result.Data.Should().ContainSingle().Which.IsPrimary.Should().BeTrue();
    }

    private async Task<ApiResponse<List<ProductImageDto>>> HandleAsync(Guid productId, params IFormFile[] files)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var fileStorageSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileStorageSettings>>();

        var handler = new UploadMultipleProductImagesCommandHandler(
            context,
            new StubStorage(),
            new ImageSharpImageProcessor(fileStorageSettings, NullLogger<ImageSharpImageProcessor>.Instance),
            currentUser,
            NullLogger<UploadMultipleProductImagesCommandHandler>.Instance,
            configuration,
            fileStorageSettings);

        return await handler.Handle(
            new UploadMultipleProductImagesCommand(productId, files.ToList()), CancellationToken.None);
    }

    private static FormFile PngFile(string name) => File(name, "image/png", [0x89, 0x50, 0x4E, 0x47]);

    private static FormFile File(string name, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "Images", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private sealed class StubStorage : IFileStorageService
    {
        public Task<string> UploadFileAsync(IFormFile file, string folder, string? fileName = null, CancellationToken cancellationToken = default) =>
            Task.FromResult($"https://example.test/{Guid.NewGuid()}.png");

        public Task<string> UploadFileAsync(Stream stream, string folder, string fileName, string contentType, CancellationToken cancellationToken = default) =>
            Task.FromResult($"https://example.test/{fileName}");

        public Task<bool> DeleteFileAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string> GetSignedUrlAsync(string fileKey, TimeSpan expirationTime, CancellationToken cancellationToken = default) => Task.FromResult(fileKey);
        public Task<bool> FileExistsAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<FileMetadata?> GetFileMetadataAsync(string fileUrl, CancellationToken cancellationToken = default) => Task.FromResult<FileMetadata?>(null);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context.Add(new Product
        {
            Id = ProductId,
            Name = "F1b Product",
            BasePrice = 10m,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.MainItem,
            CreatedBy = "test",
        });

        await context.SaveChangesAsync();
    }
}
