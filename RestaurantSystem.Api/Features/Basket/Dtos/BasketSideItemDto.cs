using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Dtos;

public record BasketSideItemDto
{
    public Guid Id { get; set; }
    public Guid? SuggestedSideItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public Guid? ProductVariationId { get; set; }
    public string? VariationName { get; set; }
    public string? ImageUrl { get; set; }
    public int Quantity { get; set; }
    public int? PresentationOrder { get; set; }
    public CompositionRole? CompositionRole { get; set; }
    public decimal SubTotal { get; set; }
}
