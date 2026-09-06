using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Maintenance.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RestaurantSystem.IntegrationTests.Features.Maintenance;

public abstract class CardBackfillTestBase : IAsyncLifetime
{
    protected static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    protected static readonly Guid ProductId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
    protected const string DefaultBaseUrl = "https://images.example.test/uploads";
    protected readonly DatabaseFixture Fixture;
    protected readonly ApplicationDbContext Context;
    protected string ContentRoot { get; } = Path.Combine(Path.GetTempPath(), "card-backfill-" + Guid.NewGuid().ToString("N"));
    protected string UploadsRoot => Path.Combine(ContentRoot, "wwwroot", "uploads");
    protected string ProductDirectory => Path.Combine(UploadsRoot, "products", ProductId.ToString());

    protected CardBackfillTestBase(DatabaseFixture fixture)
    {
        Fixture = fixture;
        Context = fixture.CreateContext();
    }

    public async Task InitializeAsync()
    {
        await Fixture.ResetDatabaseAsync();
        Directory.CreateDirectory(ProductDirectory);
        Context.Products.Add(new Product
        {
            Id = ProductId,
            Name = "Backfill hostile fixture",
            BasePrice = 4m,
            CreatedAt = Epoch,
            CreatedBy = "backfill-test",
        });
        await Context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await Context.DisposeAsync();
        if (Directory.Exists(ContentRoot))
        {
            Directory.Delete(ContentRoot, recursive: true);
        }
    }

    protected ProductCardVariantBackfillService Service(
        IImageProcessor? processor = null, string baseUrl = DefaultBaseUrl, ApplicationDbContext? context = null)
    {
        var settings = Options.Create(new FileStorageSettings { Provider = "Local" });
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(e => e.ContentRootPath).Returns(ContentRoot);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LocalStorage:BaseUrl"] = baseUrl,
        }).Build();
        return new ProductCardVariantBackfillService(context ?? Context,
            processor ?? new ImageSharpImageProcessor(settings, NullLogger<ImageSharpImageProcessor>.Instance),
            settings, environment.Object, configuration, NullLogger<ProductCardVariantBackfillService>.Instance);
    }

    protected static Guid ImageId(int ordinal) => Guid.Parse($"00000000-0000-0000-0000-{ordinal:D12}");

    protected static string Url(string fileName, string baseUrl = DefaultBaseUrl, string productFormat = "D") =>
        $"{baseUrl.TrimEnd('/')}/products/{ProductId.ToString(productFormat)}/{fileName}";

    protected async Task<ProductImage> AddRowAsync(int ordinal, string url, DateTime? createdAt = null)
    {
        var row = new ProductImage
        {
            Id = ImageId(ordinal),
            ProductId = ProductId,
            Url = url,
            CreatedAt = createdAt ?? Epoch,
            CreatedBy = "backfill-test",
        };
        Context.ProductImages.Add(row);
        await Context.SaveChangesAsync();
        return row;
    }

    protected async Task WriteOriginalAsync(string fileName = "photo.png", string productFormat = "D")
    {
        var directory = Path.Combine(UploadsRoot, "products", ProductId.ToString(productFormat));
        Directory.CreateDirectory(directory);
        using var image = new Image<Rgba32>(12, 8);
        await image.SaveAsPngAsync(Path.Combine(directory, fileName));
    }

    protected string FinalPath(string stem = "photo", string productFormat = "D") =>
        Path.Combine(UploadsRoot, "products", ProductId.ToString(productFormat), stem + "-800.webp");

    protected async Task<string?> CardUrlAsync(Guid id) =>
        await Context.ProductImages.AsNoTracking().Where(i => i.Id == id).Select(i => i.CardUrl).SingleAsync();

    protected static byte[] WebpBytes(int width = 12, int height = 8)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.SaveAsWebp(stream);
        return stream.ToArray();
    }

    protected static async Task AssertValidCardAsync(string path)
    {
        (await Image.DetectFormatAsync(path)).Name.Should().Be("Webp");
        using var image = await Image.LoadAsync(path);
        image.Width.Should().Be(12);
        image.Height.Should().Be(8);
    }
}
