using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Menus.Commands.UpdateMenuSectionsCommand;
using RestaurantSystem.Api.Features.Menus.Dtos;
using RestaurantSystem.Domain.Common.Constants;

namespace RestaurantSystem.Api.Features.Menus;

public partial class MenusController
{
    /// <summary>Patch bundle sections without replacing their persisted identities.</summary>
    [HttpPatch("{id}/sections")]
    [ApiScope(ApiTokenScopes.MenuWrite)]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<MenuSectionsPatchResultDto>>> UpdateSections(
        Guid id,
        [FromBody] MenuSectionsPatchDto request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken)
    {
        if (!MenuAuthoringVersionTag.TryParse(ifMatch, out var expectedVersion))
        {
            var missingVersion = ApiResponse<MenuSectionsPatchResultDto>.Failure(
                "Send the current menu ETag in If-Match before saving sections");
            return StatusCode(StatusCodes.Status428PreconditionRequired, missingVersion);
        }

        var result = await _mediator.SendCommand(
            new UpdateMenuSectionsCommand(id, expectedVersion, request.Sections), cancellationToken);
        if (result.Success && result.Data is not null)
        {
            Response.Headers.ETag = MenuAuthoringVersionTag.Format(result.Data.AuthoringVersion);
        }

        return Ok(result);
    }
}
