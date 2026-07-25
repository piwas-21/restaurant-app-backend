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

    /// <summary>True when the scan stopped at the requested cap — re-run to continue.</summary>
    public bool Truncated { get; set; }

    public List<ImageBackfillEntryDto> Entries { get; set; } = [];
}
