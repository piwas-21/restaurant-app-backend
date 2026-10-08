using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.Modules;
using RestaurantSystem.Api.Features.KitchenBoard.Commands.CompleteKitchenBoardWorkCommand;
using RestaurantSystem.Api.Features.KitchenBoard.Dtos;
using RestaurantSystem.Api.Features.KitchenBoard.Queries.GetKitchenBoardWorkQuery;

namespace RestaurantSystem.Api.Features.KitchenBoard;

[ApiController]
[Route("api/staff/kitchen-board")]
[Authorize(Roles = "Admin,KitchenStaff")]
[RequireModule(ModuleIds.KitchenBoard)]
public sealed class KitchenBoardController(CustomMediator mediator) : ControllerBase
{
    [HttpGet("work")]
    public async Task<ActionResult<ApiResponse<KitchenBoardWorkFeedDto>>> GetWork(
        [FromQuery] GetKitchenBoardWorkQuery query, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(query, cancellationToken));

    [HttpPost("orders/{orderId:guid}/work-items/{workItemId:guid}/complete")]
    public async Task<ActionResult<ApiResponse<KitchenBoardWorkCompletionDto>>> Complete(
        Guid orderId,
        Guid workItemId,
        [FromBody] CompleteKitchenBoardWorkRequest request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new CompleteKitchenBoardWorkCommand(
            orderId,
            workItemId,
            request.Kind,
            request.ExpectedOrderVersion,
            request.ExpectedAccountRevision), cancellationToken));
}
