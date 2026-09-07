namespace RestaurantSystem.Api.Features.Maintenance.Dtos;

/// <summary>Outcome of one bounded card-variant backfill page.</summary>
public class ProductCardVariantReportDto
{
    public bool Applied { get; set; }
    public int RowsScanned { get; set; }
    public int VariantsCreated { get; set; }
    public int AlreadyPresent { get; set; }
    public int SkippedMissingFile { get; set; }
    public int SkippedUndecodable { get; set; }
    public int RowsFailed { get; set; }
    public List<Guid> FailedImageIds { get; set; } = [];
    /// <summary>True only when another candidate exists after this page.</summary>
    public bool Truncated { get; set; }
    /// <summary>Pass as continueFrom; null on completion. Advances past skips and failures too.</summary>
    public string? NextCursor { get; set; }
}
