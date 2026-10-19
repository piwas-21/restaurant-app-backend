using RestaurantSystem.Api.Features.Addresses.Commands.CreateCustomerAddressCommand;
using RestaurantSystem.Api.Features.Addresses.Commands.UpdateCustomerAddressCommand;
using RestaurantSystem.Api.Features.Addresses.Commands.RemoveCustomerAddressCommand;
using RestaurantSystem.Api.Features.Addresses.Commands.DefaultCustomerAddressCommand;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Addresses.Dtos;
using RestaurantSystem.Api.Features.Addresses.Queries.GetCustomerAddressesQuery;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Addresses;

[ApiController]
[Route("api/customer-mobile/account/addresses")]
[RequireRole(UserRole.Customer)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerMobileAddressesController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerAddressPageDto>>> Read(
        [FromQuery] GetCustomerAddressesQuery query, CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(query, cancellationToken));

    [HttpPost]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerAddressWriteResultDto>>> Create(
        [FromBody] CustomerAddressWriteRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new CreateCustomerAddressCommand(request), cancellationToken));

    [HttpPut("{id:guid}")]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerAddressWriteResultDto>>> Update(Guid id,
        [FromBody] CustomerAddressWriteRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new UpdateCustomerAddressCommand(id, request), cancellationToken));

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerAddressActionResultDto>>> Remove(Guid id,
        [FromBody] CustomerAddressActionRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new RemoveCustomerAddressCommand(id, request), cancellationToken));

    [HttpPost("{id:guid}/set-default")]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerAddressActionResultDto>>> SetDefault(Guid id,
        [FromBody] CustomerAddressActionRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(new DefaultCustomerAddressCommand(id, request), cancellationToken));
}
