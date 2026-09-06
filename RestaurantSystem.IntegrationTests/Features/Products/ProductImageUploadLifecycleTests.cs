using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Products.Commands.UploadMultipleProductImagesCommand;
using RestaurantSystem.Api.Features.Products.Commands.UploadProductImageCommand;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Products;

[Collection("Database Lane 1")]
public class ProductImageUploadLifecycleTests(DatabaseFixture databaseFixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Upload_DisposesOwnedSource_OnVariantSuccessOrFailure(bool multiple, bool fail)
    {
        await using var context = databaseFixture.CreateContext();
        var productId = await SeedProductAsync(context);
        await using var source = new TrackingStream();
        var file = File(source);
        var storage = Storage(file.Object, productId);
        var processor = new Mock<IImageProcessor>(MockBehavior.Strict);
        var generate = processor.Setup(p => p.GenerateCardVariantAsync(source, "photo.png", 800, CancellationToken.None));
        var variant = new MemoryStream([3, 4]);
        if (fail)
        {
            generate.ThrowsAsync(new IOException("Derivation failed"));
            await variant.DisposeAsync();
        }
        else
        {
            generate.ReturnsAsync(variant);
            storage.Setup(s => s.UploadFileAsync(variant, $"products/{productId}", "photo-800.webp", "image/webp", CancellationToken.None))
                .ReturnsAsync($"products/{productId}/photo-800.webp");
        }

        var success = await HandleAsync(multiple, context, productId, file.Object, storage.Object, processor.Object);

        success.Should().BeTrue("normal variant failures must leave the original upload usable");
        source.AsyncDisposeCount.Should().Be(1, "the handler opened and owns this stream");
        source.CanRead.Should().BeFalse();
        variant.CanRead.Should().BeFalse();
        file.Verify(f => f.OpenReadStream(), Times.Once);
        processor.VerifyAll();
        storage.VerifyAll();
        var saved = context.ProductImages.Local.Should().ContainSingle().Subject;
        saved.CardUrl.Should().Be(fail ? null : $"products/{productId}/photo-800.webp");
    }

    private static async Task<Guid> SeedProductAsync(ApplicationDbContext context)
    {
        var product = new Product { Id = Guid.NewGuid(), Name = "Lifecycle test", BasePrice = 10m, CreatedBy = "test" };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product.Id;
    }

    private static Mock<IFormFile> File(Stream source)
    {
        var file = new Mock<IFormFile>(MockBehavior.Strict);
        file.SetupGet(f => f.FileName).Returns("photo.png");
        file.SetupGet(f => f.ContentType).Returns("image/png");
        file.SetupGet(f => f.Length).Returns(2);
        file.Setup(f => f.OpenReadStream()).Returns(source);
        return file;
    }

    private static Mock<IFileStorageService> Storage(IFormFile file, Guid productId)
    {
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(s => s.UploadFileAsync(file, $"products/{productId}", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync($"products/{productId}/photo.png");
        return storage;
    }

    private static async Task<bool> HandleAsync(
        bool multiple, ApplicationDbContext context, Guid productId, IFormFile file,
        IFileStorageService storage, IImageProcessor processor, CancellationToken token = default)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.GetAuditIdentifier()).Returns("test");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AWS:S3:BaseUrl"] = "https://example.test",
        }).Build();
        var settings = Options.Create(new FileStorageSettings());
        if (multiple)
        {
            var handler = new UploadMultipleProductImagesCommandHandler(
                context, storage, processor, currentUser.Object, NullLogger<UploadMultipleProductImagesCommandHandler>.Instance,
                configuration, settings);
            return (await handler.Handle(new UploadMultipleProductImagesCommand(productId, [file]), token)).Success;
        }
        var single = new UploadProductImageCommandHandler(
            context, storage, processor, currentUser.Object, NullLogger<UploadProductImageCommandHandler>.Instance,
            configuration, settings);
        return (await single.Handle(new UploadProductImageCommand { ProductId = productId, Image = file }, token)).Success;
    }

    private sealed class TrackingStream() : MemoryStream([1, 2])
    {
        public int AsyncDisposeCount { get; private set; }
        public override ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return base.DisposeAsync();
        }
    }
}
