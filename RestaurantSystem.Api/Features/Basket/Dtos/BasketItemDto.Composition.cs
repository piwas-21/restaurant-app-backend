using System.Text.Json.Serialization;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Dtos;

public partial record BasketItemDto
{
    public Guid? MenuSectionItemId { get; set; }
    public Guid? ParentComponentMenuSectionItemId { get; set; }
    public QuantityBasis? QuantityBasis { get; set; }
    public ConfigurationScope? ConfigurationScope { get; set; }
    public CompositionRole? CompositionRole { get; set; }
    public string? PresentationLabel { get; set; }
    public int? PresentationOrder { get; set; }
    [JsonIgnore]
    internal Dictionary<Guid, CompositionRole>? IngredientCompositionRoles { get; set; }
}
