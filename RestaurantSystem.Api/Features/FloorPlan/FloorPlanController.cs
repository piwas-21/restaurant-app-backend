using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.FloorPlan.Commands.SaveFloorPlanCommand;
using RestaurantSystem.Api.Features.FloorPlan.Dtos;
using RestaurantSystem.Api.Features.FloorPlan.Queries.GetFloorPlanQuery;

namespace RestaurantSystem.Api.Features.FloorPlan;

[ApiController]
[Route("api/[controller]")]
public class FloorPlanController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public FloorPlanController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Get the default floor plan the guest map renders.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<FloorPlanDocumentDto>>> GetFloorPlan()
    {
        var result = await _mediator.SendQuery(new GetFloorPlanQuery());
        return result.Success ? Ok(result) : NotFound(result);
    }

    /// <summary>Save the whole floor-plan document — dims, walls, openings, items
    /// and table geometry (Admin only).</summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<FloorPlanDocumentDto>>> SaveFloorPlan(Guid id, [FromBody] FloorPlanDocumentDto document)
    {
        var result = await _mediator.SendCommand(new SaveFloorPlanCommand(id, document));
        if (result.Success)
        {
            return Ok(result);
        }

        return result.ErrorCode switch
        {
            "PlanNotFound" => NotFound(result),
            "PlanVersionConflict" => Conflict(result),
            _ => BadRequest(result),
        };
    }
}
