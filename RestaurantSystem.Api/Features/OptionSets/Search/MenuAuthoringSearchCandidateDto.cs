using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OptionSets.Search;

public sealed class MenuAuthoringSearchCandidateDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MatchSource { get; set; } = "name";
    public string? CategoryName { get; set; }
    public string? ImageUrl { get; set; }
    public decimal? BasePrice { get; set; }
    public ProductType? ProductType { get; set; }
    public Guid? ParentOfferProductId { get; set; }
    public Guid? ParentOfferVariationId { get; set; }
    public IngredientKind? IngredientKind { get; set; }
    public bool IsComponent { get; set; }
    public bool IsActive { get; set; }
    public bool IsAvailable { get; set; }
    public int? Version { get; set; }
    public int? EntryCount { get; set; }
    public int? AttachmentCount { get; set; }
}
