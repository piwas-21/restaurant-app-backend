using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace RestaurantSystem.IntegrationTests.Common;

public class CardVariantLifecycleTests
{
    [Fact]
    public async Task GenerateCardVariantAsync_CallerCancellation_PropagatesAndLeavesSourceOpen()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await using var source = new MemoryStream([1, 2, 3]);

        var action = () => CreateProcessor().GenerateCardVariantAsync(source, "photo.png", 800, cancellation.Token);

        var exception = await action.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
        source.CanRead.Should().BeTrue("the caller owns the source");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateCardVariantAsync_BufferFailure_DisposesBufferAndLeavesSourceOpen(bool cancelled)
    {
        using var cancellation = new CancellationTokenSource();
        await using var source = new FailingCopyStream(cancellation, cancelled);
        var action = () => CreateProcessor().GenerateCardVariantAsync(source, "photo.png", 800, cancellation.Token);

        if (cancelled)
        {
            await action.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            (await action()).Should().BeNull("ordinary failures remain fail-open");
        }

        source.Destination.Should().NotBeNull("the failure must occur after buffer allocation");
        source.Destination!.CanWrite.Should().BeFalse("the incomplete buffer is owned by the processor");
        source.CanRead.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateCardVariantAsync_UnrequestedCancellation_FailsOpen()
    {
        using var cancellation = new CancellationTokenSource();
        await using var source = new FailingCopyStream(cancellation, false, new OperationCanceledException());

        var result = await CreateProcessor().GenerateCardVariantAsync(source, "photo.png", 800, cancellation.Token);

        result.Should().BeNull();
        cancellation.IsCancellationRequested.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EncodeAsync_Failure_DisposesAllocatedOutput(bool cancelled)
    {
        using var cancellation = new CancellationTokenSource();
        using var image = new Image<Rgba32>(2, 2);
        var encoder = new FailingEncoder(cancellation, cancelled);
        // The private allocation seam allows a deterministic encode failure AFTER output exists,
        // without a public testing hook or a timing-dependent cancellation race.
        var method = typeof(ImageSharpImageProcessor).GetMethod("EncodeAsync", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var action = () => (Task<Stream>)method!.Invoke(null, [image, encoder, cancellation.Token])!;

        if (cancelled)
        {
            await action.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await action.Should().ThrowAsync<IOException>();
        }

        encoder.Output.Should().NotBeNull();
        encoder.Output!.CanWrite.Should().BeFalse("the failed encode must release its allocated output");
    }

    [Fact]
    public async Task GenerateCardVariantAsync_Success_ReturnsReadableOutputAndLeavesSourceOpen()
    {
        using var image = new Image<Rgba32>(4, 2);
        await using var source = new MemoryStream();
        await image.SaveAsPngAsync(source);
        source.Position = 0;

        await using var result = await CreateProcessor().GenerateCardVariantAsync(source, "photo.png", 800);

        result.Should().NotBeNull();
        result!.Position.Should().Be(0);
        using var decoded = await Image.LoadAsync(result);
        decoded.Width.Should().Be(4);
        decoded.Height.Should().Be(2);
        source.CanRead.Should().BeTrue();
    }

    private static ImageSharpImageProcessor CreateProcessor() => new(
        Options.Create(new FileStorageSettings()), NullLogger<ImageSharpImageProcessor>.Instance);

    private sealed class FailingCopyStream(
        CancellationTokenSource cancellation, bool cancel, Exception? failure = null) : MemoryStream
    {
        public Stream? Destination { get; private set; }
        public override bool CanSeek => false;

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            Destination = destination;
            destination.WriteByte(1);
            if (cancel)
            {
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
            throw failure ?? new IOException("Source read failed");
        }
    }

    private sealed class FailingEncoder(CancellationTokenSource cancellation, bool cancel) : IImageEncoder
    {
        public bool SkipMetadata { get; init; }
        public Stream? Output { get; private set; }

        public void Encode<TPixel>(Image<TPixel> image, Stream stream) where TPixel : unmanaged, IPixel<TPixel> =>
            throw new NotSupportedException("The processor must encode asynchronously");

        public async Task EncodeAsync<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Output = stream;
            stream.WriteByte(1);
            if (cancel)
            {
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
            throw new IOException("Encode failed");
        }
    }
}
