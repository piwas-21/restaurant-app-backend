using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Auth.Commands.ChangePasswordCommand;
using RestaurantSystem.Api.Features.Auth.Commands.ForgotPasswordCommand;
using RestaurantSystem.Api.Features.Auth.Commands.LoginCommand;
using RestaurantSystem.Api.Features.Auth.Commands.RefreshTokenCommand;
using RestaurantSystem.Api.Features.Auth.Commands.ResetPasswordCommand;
using RestaurantSystem.Api.Features.Auth.Commands.SendEmailVerificationCommand;
using RestaurantSystem.Api.Features.Auth.Commands.SetPasswordCommand;
using RestaurantSystem.Api.Features.Auth.Commands.VerifyEmailCommand;
using RestaurantSystem.Api.Features.Auth.Dtos;
using RestaurantSystem.Api.Features.Auth.Queries.HasPasswordQuery;

namespace RestaurantSystem.Api.Features.Auth;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly CustomMediator _mediator;

    public AuthController(CustomMediator mediator)
    {
        _mediator = mediator;
    }


    /// <summary>
    /// User login
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Google login
    /// </summary>
    [HttpPost("google-login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> GoogleLogin([FromBody] GoogleLoginCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Apple login
    /// </summary>
    /// <remarks>
    /// The only endpoint in this controller that answers a failure with a non-200: a rejected
    /// Apple token is a 400 and an unconfigured/unreachable Apple is a 503, both carrying the
    /// usual <see cref="ApiResponse{T}"/> failure body plus an <c>errorCode</c>. Deliberately not
    /// 401 — the mobile client refreshes its session on any 401, which would turn a refused login
    /// into a spurious logout.
    /// </remarks>
    [HttpPost("apple-login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> AppleLogin([FromBody] AppleLoginCommand command)
    {
        var result = await _mediator.SendCommand(command);

        if (result.Success)
        {
            return Ok(result);
        }

        return result.ErrorCode == ErrorCodes.AppleLoginUnavailable
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, result)
            : BadRequest(result);
    }

    /// <summary>
    /// Refresh access token
    /// </summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-refresh")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> RefreshToken([FromBody] RefreshTokenCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Request password reset
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("forgot-password")]
    public async Task<ActionResult<ApiResponse<string>>> ForgotPassword([FromBody] ForgotPasswordCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Reset password with token
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("forgot-password")]
    public async Task<ActionResult<ApiResponse<string>>> ResetPassword([FromBody] ResetPasswordCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Change password for authenticated user
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<string>>> ChangePassword([FromBody] ChangePasswordCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Whether the authenticated account has a password at all
    /// </summary>
    /// <remarks>
    /// A Google/Apple account has none, so change-password can never succeed for it. The caller is
    /// resolved from the bearer token; the query carries no user identifier.
    /// </remarks>
    [HttpGet("has-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<bool>>> HasPassword()
    {
        var result = await _mediator.SendQuery(new HasPasswordQuery());
        return Ok(result);
    }

    /// <summary>
    /// Set a first password on a passwordless (social-login) account
    /// </summary>
    /// <remarks>
    /// Refused with 400 <c>PasswordAlreadySet</c> when the account already has one — that case
    /// belongs to change-password, which proves knowledge of the current password.
    /// </remarks>
    [HttpPost("set-password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<string>>> SetPassword([FromBody] SetPasswordCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Send email verification
    /// </summary>
    /// <remarks>
    /// Anonymous, and it sends a real mail per call. Own per-IP partition, plus the per-address
    /// cooldown in <see cref="SendEmailVerificationCommandHandler"/> — see both for why.
    /// </remarks>
    [HttpPost("send-email-verification")]
    [AllowAnonymous]
    [EnableRateLimiting("email-verification")]
    public async Task<ActionResult<ApiResponse<string>>> SendEmailVerification([FromBody] SendEmailVerificationCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Verify email address
    /// </summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> VerifyEmail([FromBody] VerifyEmailCommand command)
    {
        var result = await _mediator.SendCommand(command);
        return Ok(result);
    }

    /// <summary>
    /// Test authentication (requires valid JWT token)
    /// </summary>
    [HttpGet("test-auth")]
    [Authorize]
    public ActionResult<ApiResponse<string>> TestAuth()
    {
        return Ok(ApiResponse<string>.SuccessWithData("You are authenticated!"));
    }

    /// <summary>
    /// Admin-only endpoint for testing authorization
    /// </summary>
    [HttpGet("admin-only")]
    [RequireAdmin]
    public ActionResult<ApiResponse<string>> AdminOnly()
    {
        return Ok(ApiResponse<string>.SuccessWithData("You are an admin!"));
    }
}
