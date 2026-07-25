namespace RestaurantSystem.Api.Features.Maintenance.Dtos;

/// <summary>
/// What the resize-on-upload pipeline would do (or did) to one image already in storage.
/// </summary>
public class ImageBackfillEntryDto
{
    /// <summary>Path under the uploads root, e.g. "products/abc.jpg".</summary>
    public required string RelativePath { get; set; }

    /// <summary>Publicly served URL of the file as it stands today.</summary>
    public required string OriginalUrl { get; set; }

    /// <summary>
    /// Publicly served URL of the resized candidate, written to the preview folder on a dry run so
    /// the result can be compared side by side before anything is overwritten. Null once applied
    /// (the original URL then serves the new bytes) or when the file was skipped.
    /// </summary>
    public string? PreviewUrl { get; set; }

    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }
    public long OriginalBytes { get; set; }

    public int NewWidth { get; set; }
    public int NewHeight { get; set; }
    public long NewBytes { get; set; }

    /// <summary>Bytes saved (never negative — a file that would grow is skipped instead).</summary>
    public long BytesSaved => Math.Max(0, OriginalBytes - NewBytes);

    /// <summary>
    /// "resized", "recompressed", "skipped-no-gain", "skipped-unprocessable", "needs-review", or
    /// "failed". <c>needs-review</c> means the result looked like a failed decode (far too few
    /// bytes per pixel) and was deliberately NOT written — inspect that file by hand.
    /// </summary>
    public required string Outcome { get; set; }
}
