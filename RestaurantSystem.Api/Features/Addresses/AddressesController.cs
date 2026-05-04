using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Addresses.Commands.CreateAddressCommand;
using RestaurantSystem.Api.Features.Addresses.Commands.DeleteAddressCommand;
using RestaurantSystem.Api.Features.Addresses.Commands.SetDefaultAddressCommand;
using RestaurantSystem.Api.Features.Addresses.Commands.UpdateAddressCommand;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Api.Features.Addresses.Queries.GetAddressByIdQuery;
using RestaurantSystem.Api.Features.Addresses.Queries.GetUserAddressesQuery;

namespace RestaurantSystem.Api.Features.Addresses;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AddressesController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public AddressesController(CustomMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all addresses for the current user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<AddressDto>>>> GetMyAddresses()
    {
        var query = new GetUserAddressesQuery();
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Get address by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<AddressDto>>> GetAddress(Guid id)
    {
        var query = new GetAddressByIdQuery(id);
        var result = await _mediator.SendQuery(query);
        return Ok(result);
    }

    /// <summary>
    /// Create a new address
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<AddressDto>>> CreateAddress([FromBody] CreateAddressCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Create a new address
    /// </summary>
    [HttpPost("{id}/set-default")]
    public async Task<ActionResult<ApiResponse<string>>> CreateAddress(Guid id)
    {
        var command = new SetDefaultAddressCommand(id);

        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Update an address
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<AddressDto>>> UpdateAddress(
        Guid id,
        [FromBody] UpdateAddressCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest(ApiResponse<AddressDto>.Failure("Address ID mismatch"));
        }

        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Delete an address
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<string>>> DeleteAddress(Guid id)
    {
        var command = new DeleteAddressCommand(id);
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }
}
