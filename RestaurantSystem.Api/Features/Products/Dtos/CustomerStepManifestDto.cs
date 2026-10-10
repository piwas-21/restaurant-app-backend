using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products.Dtos;

public enum CustomerStepKind
{
    ProductVariation,
    ProductIngredient,
    ProductCustomizationGroup,
    ProductSauce,
    ProductSuggestedSide,
    BundleSection,
    BundleComponentVariation,
    BundleComponentIngredient,
    BundleComponentCustomizationGroup,
    BundleComponentSauce,
    BundleComponentSide
}

public sealed record CustomerStepManifestDto
{
    public required int SchemaVersion { get; init; }
    public required int Revision { get; init; }
    public required List<CustomerStepManifestStepDto> Steps { get; init; }
}

public sealed record CustomerStepManifestStepDto
{
    public required CustomerStepKind Kind { get; init; }
    public Guid? TargetId { get; init; }
    public Guid? SectionId { get; init; }
    public Guid? SectionItemId { get; init; }
    public Guid? ProductId { get; init; }
    public Guid? ScopeId { get; init; }
    public Guid? ParentComponentId { get; init; }
    public CompositionRole? CompositionRole { get; init; }
    public string? PresentationLabel { get; init; }
    public int PresentationOrder { get; init; }
}
