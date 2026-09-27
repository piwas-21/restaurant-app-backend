namespace RestaurantSystem.Api.Features.Products.Dtos;

public sealed record ProductParentBundlesDto
{
    public required List<ProductParentBundleDto> Items { get; init; }
}

public sealed record ProductParentBundleDto
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public bool IsActive { get; init; }
    public required List<ProductParentBundleReferenceDto> References { get; init; }
}

public sealed record ProductParentBundleReferenceDto
{
    public Guid SectionId { get; init; }
    public Guid? ProductVariationId { get; init; }
}
