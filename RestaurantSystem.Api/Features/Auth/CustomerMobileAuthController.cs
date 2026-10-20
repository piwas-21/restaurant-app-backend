using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Commands.RequestNativeVerificationCommand;
using RestaurantSystem.Api.Features.Auth.Commands.RequestNativePasswordResetCommand;
using RestaurantSystem.Api.Features.Auth.Commands.CompleteNativeVerificationCommand;
using RestaurantSystem.Api.Features.Auth.Commands.CompleteNativePasswordResetCommand;
using RestaurantSystem.Api.Features.Auth.Commands.RegisterNativeCustomerCommand;

namespace RestaurantSystem.Api.Features.Auth;

[ApiController]
[Route("api/customer-mobile/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerMobileAuthController(CustomMediator mediator) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    [EnableRateLimiting("register")]
    public async Task<ActionResult<ApiResponse<string>>> Register(
        [FromBody] RegisterNativeCustomerCommand command, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(command, cancellationToken));

    [AllowAnonymous]
    [HttpPost("request-verification")]
    [EnableRateLimiting("email-verification")]
    public async Task<ActionResult<ApiResponse<string>>> RequestVerification(
        [FromBody] RequestNativeVerificationCommand command, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(command, cancellationToken));

    [AllowAnonymous]
    [HttpPost("request-password-reset")]
    [EnableRateLimiting("forgot-password")]
    public async Task<ActionResult<ApiResponse<string>>> RequestPasswordReset(
        [FromBody] RequestNativePasswordResetCommand command, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(command, cancellationToken));

    [AllowAnonymous]
    [HttpPost("verify")]
    [EnableRateLimiting("email-verification")]
    public async Task<ActionResult<ApiResponse<string>>> CompleteVerification(
        [FromBody] CompleteNativeVerificationCommand command, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(command, cancellationToken));

    [AllowAnonymous]
    [HttpPost("reset-password")]
    [EnableRateLimiting("forgot-password")]
    public async Task<ActionResult<ApiResponse<string>>> CompletePasswordReset(
        [FromBody] CompleteNativePasswordResetCommand command, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(command, cancellationToken));
}
