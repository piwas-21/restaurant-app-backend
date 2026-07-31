using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Categories.Commands.UpdateCategoryImageCommand;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.IntegrationTests.Features.Categories;

/// <summary>
/// §9.12 — `UpdateCategoryImageCommand` carried the same missing-`ThenInclude` NRE as
/// `UpdateCategoryCommand` (#231) and was fixed there BY INSPECTION, with no test.
/// </summary>
/// <remarks>
/// <para>
/// The defect is worth restating because it is invisible in the obvious test: `ProductCount`
/// dereferences `pc.Product` in memory after materialisation, and lazy loading is off, so the
/// navigation is null and the command 500s — but ONLY for a category that has products. Every
/// existing category test creates a brand-new EMPTY category, where the `Count` lambda never runs.
/// That is exactly why #231 survived to reach production, and why a mutation check confirmed the
/// existing tests do not catch its regression.
/// </para>
/// <para>
/// Covering it needed the suite's first multipart-ish upload through image validation, which is why
/// it was deferred. It is done here with a stub `IFileStorageService` so the test exercises the
/// handler's own logic — validation, the include, the mapping — rather than S3.
/// </para>
/// </remarks>
public class UpdateCategoryImageCommandTests : IntegrationTestBase
{
    public UpdateCategoryImageCommandTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private static readonly Guid CategoryWithProductsId = Guid.NewGuid();

    [Fact]
    public async Task UpdatingTheImageOfACategoryThatHASProducts_Succeeds()
    {
        // THE regression test. Pre-#231 this threw a NullReferenceException — a 500 on an ordinary
        // admin action — while the same command against an empty category passed happily.
        var result = await HandleAsync(CategoryWithProductsId, PngFile());

        result.Success.Should().BeTrue(result.Message);
        result.Data!.ImageUrl.Should().Be("https://example.test/image.png");
        result.Data.ProductCount.Should().Be(1, "the count is computed from the loaded navigation");
    }

    [Fact]
    public async Task RejectsAFileWhoseEXTENSIONIsNotAllowed()
    {
        var result = await HandleAsync(CategoryWithProductsId, File("evil.exe", "image/png", [1, 2, 3]));

        result.Success.Should().BeFalse();
        // The reason lives in `errors[0]`, not `message` — `ApiResponse.Failure(error)` sets
        // `message = "Operation failed"`. Asserting on `Message` here would have passed for the
        // WRONG rejection (§9.4 documents the same trap on the client side).
        result.Errors.Should().ContainMatch("*File type not allowed*");
    }

    [Fact]
    public async Task RejectsAFileWhoseMIMETypeIsNotAllowed()
    {
        // Extension and MIME are checked separately on purpose: a .png named file claiming
        // text/html is the interesting half of the pair.
        var result = await HandleAsync(CategoryWithProductsId, File("image.png", "text/html", [1, 2, 3]));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainMatch("*MIME type*");
    }

    [Fact]
    public async Task RejectsAnEmptyFile()
    {
        var result = await HandleAsync(CategoryWithProductsId, File("image.png", "image/png", []));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainMatch("*No image file provided*");
    }

    [Fact]
    public async Task ReportsAMissingCategoryRatherThanThrowing()
    {
        var result = await HandleAsync(Guid.NewGuid(), PngFile());

        result.Success.Should().BeFalse();
    }

    private async Task<ApiResponse<Api.Features.Categories.Dtos.CategoryDto>> HandleAsync(
        Guid categoryId, IFormFile file)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var fileStorageSettings = scope.ServiceProvider.GetRequiredService<IOptions<FileStorageSettings>>();

        var handler = new UpdateCategoryImageCommandHandler(
            context,
            new StubStorage(),
            currentUser,
            NullLogger<UpdateCategoryImageCommandHandler>.Instance,
            configuration,
            fileStorageSettings);

        return await handler.Handle(new UpdateCategoryImageCommand(categoryId, file), CancellationToken.None);
    }

    private static FormFile PngFile() => File("image.png", "image/png", [0x89, 0x50, 0x4E, 0x47]);

    private static FormFile File(string name, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "image", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private sealed class StubStorage : IFileStorageService
    {
        public Task<string> UploadFileAsync(IFormFile file, string folder, string? fileName = null, CancellationToken cancellationToken = default) =>
            Task.FromResult("https://example.test/image.png");

        public Task<string> UploadFileAsync(Stream stream, string folder, string fileName, string contentType, CancellationToken cancellationToken = default) =>
            Task.FromResult("https://example.test/image.png");

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

        // A category WITH a product — the state every existing category test omits, and the only
        // state in which the missing ThenInclude was observable.
        var category = new Category { Id = CategoryWithProductsId, Name = "§9.12 Has Products", CreatedBy = "test" };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "§9.12 Product",
            BasePrice = 10m,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.MainItem,
            CreatedBy = "test",
        };
        product.ProductCategories.Add(new ProductCategory { Category = category, IsPrimary = true, CreatedBy = "test" });

        context.AddRange(category, product);
        await context.SaveChangesAsync();
    }
}
