namespace RestaurantSystem.Api.Features.Maintenance.Dtos;

/// <summary>Outcome of one card-variant backfill run. Paged, like the resize backfill it mirrors.</summary>
public class ProductCardVariantReportDto
{
    public bool Applied { get; set; }
    public int RowsScanned { get; set; }
    public int VariantsCreated { get; set; }
    public int AlreadyPresent { get; set; }
    public int SkippedMissingFile { get; set; }
    public int SkippedUndecodable { get; set; }
    /// <summary>Set when the row cap stopped the walk early; null when the walk completed.</summary>
    public bool Truncated { get; set; }
}
