using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Basket.Dtos.Requests;

public record CustomizationGroupSelectionDto
{
    public Guid GroupId { get; init; }
    public List<CustomizationOptionSelectionDto> Options { get; init; } = [];
}

public record CustomizationOptionSelectionDto
{
    public CustomizationOptionKind Kind { get; init; }
    public Guid OptionId { get; init; }
    public int Quantity { get; init; } = 1;
}
