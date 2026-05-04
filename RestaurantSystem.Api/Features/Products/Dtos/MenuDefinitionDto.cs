namespace RestaurantSystem.Api.Features.Products.Dtos;

public record MenuDefinitionDto
{
    public Guid? Id { get; init; }
    public bool IsAlwaysAvailable { get; init; }
    public TimeSpan? StartTime { get; init; }
    public TimeSpan? EndTime { get; init; }

    public bool AvailableMonday { get; init; }
    public bool AvailableTuesday { get; init; }
    public bool AvailableWednesday { get; init; }
    public bool AvailableThursday { get; init; }
    public bool AvailableFriday { get; init; }
    public bool AvailableSaturday { get; init; }
    public bool AvailableSunday { get; init; }

    public List<MenuSectionDto> Sections { get; init; } = new();
}
