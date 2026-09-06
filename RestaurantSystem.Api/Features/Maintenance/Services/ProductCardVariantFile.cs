using RestaurantSystem.Api.Common.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;

namespace RestaurantSystem.Api.Features.Maintenance.Services;

/// <summary>A final path is never visible until the whole, validated card has been written.</summary>
internal static class ProductCardVariantFile
{
    public static async Task<bool> IsValidAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var source = File.OpenRead(path);
            var info = await Image.IdentifyAsync(source, cancellationToken);
            if (info.Metadata.DecodedImageFormat is not WebpFormat
                || info.Width <= 0 || info.Height <= 0
                || Math.Max(info.Width, info.Height) > ProductImageCardVariants.EdgePixels
                || info.FrameMetadataCollection.Count > 1 // animated WebP is not a still card; stills carry 0 frame entries
            )
            {
                return false;
            }

            source.Position = 0;
            // Identify checks the header only. Decode too, within the bounded card dimensions.
            using var image = await Image.LoadAsync(new DecoderOptions { MaxFrames = 1 }, source, cancellationToken);
            return image.Width == info.Width && image.Height == info.Height;
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or FileNotFoundException)
        {
            return false;
        }
    }

    public static async Task PublishAsync(
        Stream variant, string destination, ProductCardVariantPaths paths, CancellationToken cancellationToken)
    {
        paths.EnsureSafe(destination);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await variant.CopyToAsync(target, cancellationToken);
            }

            if (!await IsValidAsync(temp, cancellationToken))
            {
                throw new IOException("Generated card variant is not a complete, bounded WebP image.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            paths.EnsureSafe(destination);
            // Each competing run publishes a complete file; none opens/truncates the final path.
            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
