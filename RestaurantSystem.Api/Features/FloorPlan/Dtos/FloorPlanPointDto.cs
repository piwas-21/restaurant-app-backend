namespace RestaurantSystem.Api.Features.FloorPlan.Dtos;

/// <summary>A wall vertex in metres (origin top-left).</summary>
public record FloorPlanPointDto
{
    public decimal X { get; set; }
    public decimal Y { get; set; }
}
