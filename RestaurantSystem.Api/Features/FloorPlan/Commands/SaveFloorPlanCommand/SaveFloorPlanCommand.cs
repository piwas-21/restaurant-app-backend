using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FloorPlan.Dtos;
using RestaurantSystem.Api.Features.FloorPlan.Services;

namespace RestaurantSystem.Api.Features.FloorPlan.Commands.SaveFloorPlanCommand;

/// <summary>Saves the whole floor-plan document — plan dims, walls, openings,
/// items and table geometry (FLOOR-PLAN-REVAMP §5.2).</summary>
public record SaveFloorPlanCommand(Guid PlanId, FloorPlanDocumentDto Document) : ICommand<ApiResponse<FloorPlanDocumentDto>>;

public class SaveFloorPlanCommandHandler : ICommandHandler<SaveFloorPlanCommand, ApiResponse<FloorPlanDocumentDto>>
{
    private readonly IFloorPlanService _service;
    private readonly ILogger<SaveFloorPlanCommandHandler> _logger;

    public SaveFloorPlanCommandHandler(IFloorPlanService service, ILogger<SaveFloorPlanCommandHandler> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task<ApiResponse<FloorPlanDocumentDto>> Handle(SaveFloorPlanCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _service.SaveAsync(command.PlanId, command.Document, cancellationToken);
            if (result.Success)
            {
                _logger.LogInformation("Saved floor plan {PlanId}", command.PlanId);
                return ApiResponse<FloorPlanDocumentDto>.SuccessWithData(result.Document!, "Floor plan saved");
            }

            return ApiResponse<FloorPlanDocumentDto>.FailureWithCode(result.Error!, result.ErrorCode!, "Failed to save floor plan");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving floor plan {PlanId}", command.PlanId);
            return ApiResponse<FloorPlanDocumentDto>.Failure("Failed to save floor plan");
        }
    }
}
