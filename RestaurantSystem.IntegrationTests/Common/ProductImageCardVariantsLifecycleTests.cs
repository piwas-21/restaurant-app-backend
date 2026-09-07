using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;

namespace RestaurantSystem.IntegrationTests.Common;

public class ProductImageCardVariantsLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateAndStoreAsync_StorageOutcome_DisposesVariantButNotSource(bool fail)
    {
        await using var source = new MemoryStream([1, 2]);
        var variant = new MemoryStream([3, 4]);
        var processor = Processor(source, variant);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        var upload = storage.Setup(s => s.UploadFileAsync(
            variant, "products/test", "photo-800.webp", "image/webp", CancellationToken.None));
        if (fail)
        {
            upload.ThrowsAsync(new IOException("Storage unavailable"));
        }
        else
        {
            upload.ReturnsAsync("products/test/photo-800.webp");
        }

        var result = await ProductImageCardVariants.GenerateAndStoreAsync(
            storage.Object, processor.Object, "products/test", "photo.jpg", source, NullLogger.Instance);

        result.Should().Be(fail ? null : "products/test/photo-800.webp");
        storage.VerifyAll();
        processor.VerifyAll();
        variant.CanRead.Should().BeFalse();
        source.CanRead.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateAndStoreAsync_ProcessorCancellation_OnlyPropagatesWhenCallerRequested(bool requested)
    {
        using var cancellation = new CancellationTokenSource();
        await using var source = new MemoryStream([1, 2]);
        var processor = new Mock<IImageProcessor>(MockBehavior.Strict);
        processor.Setup(p => p.GenerateCardVariantAsync(source, "photo.jpg", 800, cancellation.Token))
            .Returns(() =>
            {
                if (requested)
                {
                    cancellation.Cancel();
                }
                return Task.FromException<Stream?>(new OperationCanceledException(cancellation.Token));
            });
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        var action = () => ProductImageCardVariants.GenerateAndStoreAsync(
            storage.Object, processor.Object, "products/test", "photo.jpg", source, NullLogger.Instance, cancellation.Token);

        if (requested)
        {
            var exception = await action.Should().ThrowAsync<OperationCanceledException>();
            exception.Which.CancellationToken.Should().Be(cancellation.Token);
        }
        else
        {
            (await action()).Should().BeNull();
        }
        processor.VerifyAll();
        storage.VerifyNoOtherCalls();
        source.CanRead.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateAndStoreAsync_StorageCancellation_PropagatesAndDisposesVariant()
    {
        using var cancellation = new CancellationTokenSource();
        await using var source = new MemoryStream([1, 2]);
        var variant = new MemoryStream([3, 4]);
        var processor = Processor(source, variant, cancellation.Token);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(s => s.UploadFileAsync(variant, "products/test", "photo-800.webp", "image/webp", cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<string>(cancellation.Token);
            });
        var action = () => ProductImageCardVariants.GenerateAndStoreAsync(
            storage.Object, processor.Object, "products/test", "photo.jpg", source, NullLogger.Instance, cancellation.Token);

        var exception = await action.Should().ThrowAsync<OperationCanceledException>();

        exception.Which.CancellationToken.Should().Be(cancellation.Token);
        storage.VerifyAll();
        processor.VerifyAll();
        variant.CanRead.Should().BeFalse();
        source.CanRead.Should().BeTrue();
    }

    private static Mock<IImageProcessor> Processor(Stream source, Stream variant, CancellationToken token = default)
    {
        var processor = new Mock<IImageProcessor>(MockBehavior.Strict);
        processor.Setup(p => p.GenerateCardVariantAsync(source, "photo.jpg", 800, token)).ReturnsAsync(variant);
        return processor;
    }
}
