using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Products.Dtos;

public record ProductIngredientDto
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsOptional { get; set; }
    public decimal Price { get; set; }
    public bool IsIncludedInBasePrice { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public int MaxQuantity { get; set; }

    // Provenance: the global library row this ingredient was copied from, or null when the admin
    // typed it by hand. Read-write — the field was absent until S2, so the id an admin picker sent
    // was dropped by the model binder and the link could never be persisted at all.
    public Guid? GlobalIngredientId { get; set; }

    // Ingredient or sauce (S5). Additive and defaulted: a client that omits `kind` keeps sending
    // ingredients, which is what every client did before this field existed. Rationale for the
    // discriminator shape — and why a second entity was rejected — is on `IngredientKind` itself.
    public IngredientKind Kind { get; set; } = IngredientKind.Ingredient;

    public Dictionary<string, ProductIngredientContentDto>? Content { get; set; }
}

public record ProductIngredientContentDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
}
