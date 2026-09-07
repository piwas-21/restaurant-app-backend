using Microsoft.Extensions.Logging;
using RestaurantSystem.Api.Common.Services.Interfaces;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// One place owns the card-variant naming and storage contract, because TWO writers must agree on
/// it byte-for-byte — the upload commands (which generate the variant while the bytes are in hand)
/// and the maintenance backfill (which generates it for rows that predate the feature). A card
/// variant is the ORIGINAL's filename with the extension replaced by <see cref="Suffix"/>, in the
/// same folder; derivation from the stored URL is therefore total and needs no database column
/// beyond <c>ProductImage.CardUrl</c> recording that it exists.
/// </summary>
public static class ProductImageCardVariants
{
    /// <summary>Card render targets are 120-400 CSS px; 800 px covers a 400 px card at 2x DPR.</summary>
    public const int EdgePixels = 800;

    public const string Suffix = "-800.webp";
    public const string ContentType = "image/webp";

    /// <summary><c>products/1df078d9/1788458765_4ea5e462.jpg</c> → <c>1788458765_4ea5e462-800.webp</c></summary>
    public static string VariantFileName(string storedFileName)
    {
        var stem = Path.GetFileNameWithoutExtension(storedFileName);
        return $"{stem}{Suffix}";
    }

    /// <summary>
    /// Generates the card variant for an upload and stores it beside the original. Fail-open:
    /// failures other than caller cancellation return <c>null</c> and leave <c>CardUrl</c> null — the guest
    /// then gets the original, which is the pre-feature behaviour and never a broken image.
    /// The stream overload of <see cref="IFileStorageService.UploadFileAsync"/> bypasses the
    /// processing decorator by design: the original is already the processed artefact.
    /// </summary>
    public static async Task<string?> GenerateAndStoreAsync(
        IFileStorageService storage,
        IImageProcessor processor,
        string folder,
        string storedFileName,
        Stream originalSource,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var variant = await processor.GenerateCardVariantAsync(
                originalSource, storedFileName, EdgePixels, cancellationToken);
            if (variant is null)
            {
                return null;
            }

            await using (variant)
            {
                return await storage.UploadFileAsync(
                    variant, folder, VariantFileName(storedFileName), ContentType, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Card variant generation failed for {Folder}/{File}; serving the original",
                folder, storedFileName);
            return null;
        }
    }
}
