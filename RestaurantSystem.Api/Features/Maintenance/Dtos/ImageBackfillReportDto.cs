namespace RestaurantSystem.Api.Features.Maintenance.Dtos;

/// <summary>
/// Result of a resize backfill over the images already in storage.
/// </summary>
public class ImageBackfillReportDto
{
    /// <summary>False = nothing was written over; resized candidates went to the preview folder.</summary>
    public bool Applied { get; set; }

    /// <summary>Longest-edge bound and encoder quality used — the same FileStorage settings uploads use.</summary>
    public int MaxImageEdgePixels { get; set; }
    public int ImageQuality { get; set; }

    public int FilesScanned { get; set; }
    public int FilesChanged { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesFailed { get; set; }

    public long TotalOriginalBytes { get; set; }
    public long TotalNewBytes { get; set; }
    public long TotalBytesSaved { get; set; }

    /// <summary>True when the scan stopped at the cap. Continue with <see cref="NextCursor"/>.</summary>
    public bool Truncated { get; set; }

    /// <summary>
    /// Where to resume: pass it back as <c>continueFrom</c> to process the next window. Null means
    /// there is nothing to resume from — either the walk finished, or it was capped at zero and
    /// stopped before processing anything, in which case a resume IS a fresh start. Non-null
    /// otherwise, and only ever when <see cref="Truncated"/> is true.
    ///
    /// <para>A bare re-run does NOT continue, which is what #280 was: the scan restarts from the
    /// first file every time and the cap counts skips, so without this the images past the first
    /// window were unreachable however often the endpoint was called.</para>
    /// </summary>
    public string? NextCursor { get; set; }

    public List<ImageBackfillEntryDto> Entries { get; set; } = [];
}
