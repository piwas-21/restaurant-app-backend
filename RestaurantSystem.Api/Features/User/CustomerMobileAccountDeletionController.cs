using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Services.Operations;
using RestaurantSystem.Api.Features.User.Commands.RequestCustomerAccountDeletionCommand;
using RestaurantSystem.Api.Features.User.Dtos;
using RestaurantSystem.Api.Features.User.Queries.GetCustomerAccountDeletionQuery;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.User;

[ApiController]
[Route("api/customer-mobile/account/deletion")]
[RequireRole(UserRole.Customer)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerMobileAccountDeletionController(CustomMediator mediator) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(CustomerBasketOperationRegistration.AddPolicyName)]
    public async Task<ActionResult<ApiResponse<CustomerAccountDeletionDto>>> Read(CancellationToken cancellationToken) =>
        Ok(await mediator.SendQuery(new GetCustomerAccountDeletionQuery(), cancellationToken));

    [HttpPost("request")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<CustomerAccountDeletionDto>>> Schedule(
        [FromBody] RequestCustomerAccountDeletionCommand command, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(command, cancellationToken));
}
