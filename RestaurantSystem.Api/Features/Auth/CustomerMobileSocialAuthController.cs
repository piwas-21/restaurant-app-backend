using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.NativeSocial;
using RestaurantSystem.Api.Features.Auth.Commands.CreateNativeGoogleChallengeCommand;
using RestaurantSystem.Api.Features.Auth.Commands.CreateNativeGoogleLinkChallengeCommand;
using RestaurantSystem.Api.Features.Auth.Commands.CompleteNativeGoogleLoginCommand;
using RestaurantSystem.Api.Features.Auth.Commands.CompleteNativeGoogleLinkCommand;

namespace RestaurantSystem.Api.Features.Auth;

[ApiController]
[Route("api/customer-mobile/auth/google")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("auth")]
public sealed class CustomerMobileSocialAuthController(CustomMediator mediator) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("challenge")]
    public async Task<ActionResult<ApiResponse<NativeSocialChallengeDto>>> Challenge(
        [FromBody] CreateNativeGoogleChallengeCommand command, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(command, cancellationToken));

    [AllowAnonymous]
    [HttpPost("complete")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Complete(
        [FromBody] CompleteNativeGoogleLoginCommand command, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(command, cancellationToken));

    [Authorize(Roles = "Customer")]
    [HttpPost("link/challenge")]
    public async Task<ActionResult<ApiResponse<NativeSocialChallengeDto>>> LinkChallenge(
        [FromBody] CreateNativeGoogleLinkChallengeCommand command, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(command, cancellationToken));

    [Authorize(Roles = "Customer")]
    [HttpPost("link/complete")]
    public async Task<ActionResult<ApiResponse<string>>> LinkComplete(
        [FromBody] CompleteNativeGoogleLinkCommand command, CancellationToken cancellationToken) =>
        Ok(await mediator.SendCommand(command, cancellationToken));
}
