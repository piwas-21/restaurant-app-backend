using RestaurantSystem.Api.Features.FloorPlan.Dtos;

namespace RestaurantSystem.Api.Features.FloorPlan.Services;

/// <summary>Reads and persists the whole floor-plan document (FLOOR-PLAN-REVAMP §5.2).</summary>
public interface IFloorPlanService
{
    /// <summary>The default plan the guest map renders, or null if none exists.</summary>
    Task<FloorPlanDocumentDto?> GetDefaultAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the plan's walls, openings and items and applies table geometry,
    /// with optimistic concurrency on <see cref="FloorPlanDocumentDto.UpdatedAt"/>.
    /// </summary>
    Task<SaveFloorPlanResult> SaveAsync(Guid planId, FloorPlanDocumentDto document, CancellationToken cancellationToken);
}

/// <summary>Outcome of a save. <see cref="ErrorCode"/> maps to the HTTP status.</summary>
public record SaveFloorPlanResult(FloorPlanDocumentDto? Document, string? ErrorCode, string? Error)
{
    public bool Success => ErrorCode is null;

    public static SaveFloorPlanResult Ok(FloorPlanDocumentDto document) => new(document, null, null);
    public static SaveFloorPlanResult NotFound(string error) => new(null, "PlanNotFound", error);
    public static SaveFloorPlanResult Conflict(string error) => new(null, "PlanVersionConflict", error);
}
