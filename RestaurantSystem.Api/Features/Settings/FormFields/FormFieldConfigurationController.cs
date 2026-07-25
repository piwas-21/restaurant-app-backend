using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Settings.FormFields.Commands.UpdateFormFieldConfigurationsCommand;
using RestaurantSystem.Api.Features.Settings.FormFields.Dtos;
using RestaurantSystem.Api.Features.Settings.FormFields.Queries.GetFormFieldConfigurationsQuery;

namespace RestaurantSystem.Api.Features.Settings.FormFields;

[ApiController]
[Route("api/[controller]")]
public class FormFieldConfigurationController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public FormFieldConfigurationController(CustomMediator mediator) => _mediator = mediator;

    /// <summary>
    /// All customer form field configurations, grouped per form. Public — customer-facing
    /// forms consult this before render (precedent: OrderTypeConfiguration/enabled).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<List<FormFieldsDto>>>> GetAll(
        CancellationToken cancellationToken)
        => Ok(await _mediator.SendQuery(new GetFormFieldConfigurationsQuery(), cancellationToken));

    /// <summary>
    /// Bulk update of configurable fields (admin only). Unknown pairs, locked-field
    /// changes and required-but-hidden combinations are rejected with 400.
    /// </summary>
    [HttpPut]
    [RequireAdmin]
    public async Task<ActionResult<ApiResponse<List<FormFieldsDto>>>> Update(
        [FromBody] UpdateFormFieldConfigurationsCommand command,
        CancellationToken cancellationToken)
        => Ok(await _mediator.SendCommand(command, cancellationToken));
}
