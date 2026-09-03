namespace RestaurantSystem.Api.Features.Products.Dtos;

public record ProductVariationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal PriceModifier { get; init; }
    public decimal FinalPrice { get; init; }
    public bool IsActive { get; init; }
    public int DisplayOrder { get; init; }

    /// <summary>
    /// The library row this variation was copied from, or null (plan S4). PROVENANCE only — nothing
    /// reads the library row afterwards. A client that round-trips the DTO must send it back, or
    /// the link is re-decided by the NAME: since D14 an unlinked name is matched against the
    /// library, or promoted into it, so what is cleared is the claim to a row the name no longer
    /// matches — not the link itself. Same on the ingredient side.
    /// </summary>
    public Guid? GlobalVariationId { get; init; }

    public Dictionary<string, ProductVariationContentDto>? Content { get; init; }
}

public record ProductVariationContentDto
{
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
}
