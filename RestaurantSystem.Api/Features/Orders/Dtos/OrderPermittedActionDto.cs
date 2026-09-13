namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>One explainable order action for the authenticated caller.</summary>
public record OrderPermittedActionDto
{
    public string Action { get; init; } = string.Empty;
    public bool Allowed { get; init; }
    public string? ReasonCode { get; init; }
    public bool RequiresReason { get; init; }
}
